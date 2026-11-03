//go:build windows

package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"log/slog"
	"os"

	"github.com/support360/support360/agent/internal/agent"
	"golang.org/x/sys/windows/svc"
)

type serviceConfig struct {
	Server   string `json:"server"`
	Identity string `json:"identity"`
	Queue    string `json:"queue"`
}

func runService(args []string) error {
	fs := flag.NewFlagSet("service", flag.ContinueOnError)
	configPath := fs.String("config", "", "path to service configuration JSON")
	if err := fs.Parse(args); err != nil {
		return err
	}
	if *configPath == "" {
		return errors.New("-config is required")
	}
	data, err := os.ReadFile(*configPath)
	if err != nil {
		return err
	}
	var config serviceConfig
	if err := json.Unmarshal(data, &config); err != nil {
		return errors.New("service configuration is invalid JSON")
	}
	if config.Server == "" || config.Identity == "" || config.Queue == "" {
		return errors.New("service configuration is incomplete")
	}
	return svc.Run("Support360Agent", &support360Service{config: config})
}

type support360Service struct{ config serviceConfig }

func (s *support360Service) Execute(_ []string, requests <-chan svc.ChangeRequest, changes chan<- svc.Status) (bool, uint32) {
	changes <- svc.Status{State: svc.StartPending}
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() {
		identity, err := agent.ReadIdentity(s.config.Identity)
		if err == nil {
			var client *agent.Client
			client, err = agent.NewClient(s.config.Server, false)
			if err == nil {
				err = agent.Run(ctx, client, identity, s.config.Queue)
			}
		}
		done <- err
	}()
	changes <- svc.Status{State: svc.Running, Accepts: svc.AcceptStop | svc.AcceptShutdown}
	for {
		select {
		case request := <-requests:
			switch request.Cmd {
			case svc.Interrogate:
				changes <- svc.Status{State: svc.Running, Accepts: svc.AcceptStop | svc.AcceptShutdown}
			case svc.Stop, svc.Shutdown:
				changes <- svc.Status{State: svc.StopPending}
				cancel()
				err := <-done
				if err != nil {
					slog.Error("agent service stopped with error", "error", err)
					changes <- svc.Status{State: svc.Stopped, Win32ExitCode: 1}
					return false, 1
				}
				changes <- svc.Status{State: svc.Stopped}
				return false, 0
			}
		case err := <-done:
			cancel()
			changes <- svc.Status{State: svc.Stopped, Win32ExitCode: 1}
			if err != nil {
				slog.Error("agent service stopped", "error", err)
			}
			return false, 1
		}
	}
}
