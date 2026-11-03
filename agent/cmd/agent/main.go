package main

import (
	"context"
	"errors"
	"flag"
	"fmt"
	"log/slog"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"

	"github.com/support360/support360/agent/internal/agent"
)

func main() {
	if err := execute(os.Args[1:]); err != nil {
		slog.Error("agent stopped", "error", err)
		os.Exit(1)
	}
}

func execute(args []string) error {
	if len(args) == 0 {
		return errors.New("usage: support360-agent <enroll|run> [options]")
	}
	switch args[0] {
	case "enroll":
		return enroll(args[1:])
	case "run":
		return run(args[1:])
	case "service":
		return runService(args[1:])
	default:
		return fmt.Errorf("unknown command %q; expected enroll or run", args[0])
	}
}

func enroll(args []string) error {
	fs := flag.NewFlagSet("enroll", flag.ContinueOnError)
	server := fs.String("server", os.Getenv("SUPPORT360_SERVER_URL"), "Support360 API base URL")
	token := fs.String("token", "", "short-lived single-use enrollment token")
	identityPath := fs.String("identity", defaultIdentityPath(), "path to protected identity file")
	allowHTTP := fs.Bool("allow-http", false, "allow plaintext HTTP for local development only")
	deploymentOptIn := fs.Bool("enable-deployments", false, "locally opt this agent into future controlled deployment work")
	if err := fs.Parse(args); err != nil {
		return err
	}
	if *token == "" {
		*token = os.Getenv("SUPPORT360_ENROLLMENT_TOKEN")
		if *token == "" {
			return errors.New("-token or SUPPORT360_ENROLLMENT_TOKEN is required")
		}
	}
	client, err := agent.NewClient(*server, *allowHTTP)
	if err != nil {
		return err
	}
	response, err := client.Enroll(context.Background(), *token, *identityPath, *deploymentOptIn)
	if err != nil {
		return err
	}
	fmt.Printf("Enrolled agent %s. Identity saved to %s. Heartbeat interval %ds, telemetry interval %ds.\n",
		response.AgentID, *identityPath, response.HeartbeatIntervalSecond, response.TelemetryIntervalSecond)
	return nil
}

func run(args []string) error {
	fs := flag.NewFlagSet("run", flag.ContinueOnError)
	server := fs.String("server", os.Getenv("SUPPORT360_SERVER_URL"), "Support360 API base URL")
	identityPath := fs.String("identity", defaultIdentityPath(), "path to protected identity file")
	queuePath := fs.String("queue", defaultQueuePath(), "bounded local telemetry queue path")
	allowHTTP := fs.Bool("allow-http", false, "allow plaintext HTTP for local development only")
	if err := fs.Parse(args); err != nil {
		return err
	}
	identity, err := agent.ReadIdentity(*identityPath)
	if err != nil {
		return fmt.Errorf("load identity: %w", err)
	}
	client, err := agent.NewClient(*server, *allowHTTP)
	if err != nil {
		return err
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	return agent.Run(ctx, client, identity, *queuePath)
}

func defaultIdentityPath() string {
	if value := os.Getenv("SUPPORT360_IDENTITY_FILE"); value != "" {
		return value
	}
	base, err := os.UserConfigDir()
	if err != nil {
		return "support360-agent-identity.json"
	}
	return filepath.Join(base, "support360", "identity.json")
}

func defaultQueuePath() string {
	if value := os.Getenv("SUPPORT360_QUEUE_FILE"); value != "" {
		return value
	}
	base, err := os.UserCacheDir()
	if err != nil {
		return "support360-agent-queue.json"
	}
	return filepath.Join(base, "support360", "queue.json")
}
