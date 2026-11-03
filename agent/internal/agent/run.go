package agent

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"runtime"
	"time"

	"github.com/support360/support360/agent/internal/collectors/host"
	"github.com/support360/support360/agent/internal/protocol"
	"github.com/support360/support360/agent/internal/queue"
	"github.com/support360/support360/agent/internal/resultqueue"
)

func Run(ctx context.Context, client *Client, identity protocol.Identity, queuePath string) error {
	if identity.APIKey == "" || identity.AgentID == "" {
		return errors.New("agent identity is incomplete")
	}
	q, err := queue.Open(queuePath)
	if err != nil {
		return fmt.Errorf("open local event queue: %w", err)
	}
	checkQueue, err := resultqueue.Open(queuePath + ".checks")
	if err != nil {
		return fmt.Errorf("open local application result queue: %w", err)
	}
	collector := host.New()
	checks, checkErr := client.ApplicationChecks(ctx, identity.APIKey)
	if checkErr != nil {
		slog.Warn("application check configuration unavailable", "error", checkErr)
	}
	lastChecks := map[string]time.Time{}
	runChecks := func() error {
		now := time.Now()
		var batch []protocol.ApplicationCheckResult
		for _, check := range checks {
			interval := time.Duration(check.IntervalSeconds) * time.Second
			if interval < 30*time.Second {
				interval = 30 * time.Second
			}
			if last, ok := lastChecks[check.ID]; ok && now.Sub(last) < interval {
				continue
			}
			lastChecks[check.ID] = now
			checkCtx, cancel := context.WithTimeout(ctx, 12*time.Second)
			batch = append(batch, executeCheck(checkCtx, check))
			cancel()
		}
		if err := checkQueue.Add(batch); err != nil {
			return fmt.Errorf("persist application result queue: %w", err)
		}
		pending := checkQueue.Batch()
		if len(pending) == 0 {
			return nil
		}
		response, err := client.ApplicationCheckResults(ctx, identity.APIKey, pending)
		if unauthorized(err) {
			return err
		}
		if err != nil {
			slog.Warn("application check results upload failed", "error", err)
			return nil
		}
		if response.Rejected > 0 {
			slog.Warn("server rejected application check results", "rejected", response.Rejected)
		}
		ack := response.Accepted + response.Duplicates + response.Rejected
		if ack > len(pending) {
			ack = len(pending)
		}
		if err := checkQueue.Acknowledge(ack); err != nil {
			return fmt.Errorf("acknowledge application result queue: %w", err)
		}
		if checkQueue.Len() > 0 {
			slog.Info("application result queue status", "queued_events", checkQueue.Len())
		}
		return nil
	}
	config, err := client.Config(ctx, identity.APIKey)
	if err != nil {
		slog.Warn("agent configuration unavailable; using conservative local interval", "error", err)
	}
	telemetryInterval := clampInterval(config.TelemetryIntervalSeconds, 60)
	heartbeatInterval := clampInterval(config.HeartbeatIntervalSeconds, 30)
	allowed := make(map[string]bool, len(config.AllowedMetrics))
	for _, metric := range config.AllowedMetrics {
		allowed[metric] = true
	}
	if len(allowed) == 0 {
		for _, metric := range []string{"cpu.usage.percent", "memory.used.percent", "memory.used.bytes", "disk.used.percent", "disk.free.bytes", "network.rx.bytes_total", "network.tx.bytes_total", "uptime.seconds"} {
			allowed[metric] = true
		}
	}

	collectorHealthy := false
	collectorHealth := "collection has not completed yet"
	collect := func() error {
		collectCtx, cancel := context.WithTimeout(ctx, 15*time.Second)
		samples, collectErr := collector.Collect(collectCtx)
		cancel()
		if collectErr != nil {
			collectorHealthy = false
			collectorHealth = collectErr.Error()
			if len(collectorHealth) > 1000 {
				collectorHealth = collectorHealth[:1000]
			}
			slog.Warn("metric collection failed", "error", collectErr)
			return nil
		}
		collectorHealthy = true
		collectorHealth = "ok"
		filtered := samples[:0]
		for _, sample := range samples {
			if allowed[sample.Metric] {
				filtered = append(filtered, sample)
			}
		}
		if err := q.Add(filtered); err != nil {
			return fmt.Errorf("persist telemetry queue: %w", err)
		}
		if batch := q.Batch(); len(batch) > 0 {
			response, err := client.Telemetry(ctx, identity.APIKey, batch)
			if err != nil {
				if unauthorized(err) {
					return err
				}
				slog.Warn("telemetry upload failed; retaining queued events", "queue_depth", q.Len(), "error", err)
			} else {
				ack := response.Accepted + response.Duplicates + response.Rejected
				if ack > len(batch) {
					ack = len(batch)
				}
				if err := q.Acknowledge(ack); err != nil {
					return fmt.Errorf("acknowledge telemetry queue: %w", err)
				}
				if response.Rejected > 0 {
					slog.Warn("server rejected telemetry samples", "rejected", response.Rejected)
				}
			}
		}
		if q.Len() > 0 {
			slog.Info("agent queue status", "queued_events", q.Len())
		}
		return nil
	}
	heartbeat := func() error {
		err := sendHeartbeat(ctx, client, identity, collectorHealthy, collectorHealth)
		if unauthorized(err) {
			return err
		}
		if err != nil {
			slog.Warn("heartbeat upload failed", "error", err)
		}
		return nil
	}

	if err := collect(); err != nil {
		return err
	}
	if err := heartbeat(); err != nil {
		return err
	}
	if err := runChecks(); err != nil {
		return err
	}
		telemetryTicker := time.NewTicker(time.Duration(telemetryInterval) * time.Second)
	heartbeatTicker := time.NewTicker(time.Duration(heartbeatInterval) * time.Second)
	deployTicker := time.NewTicker(time.Minute)
	defer telemetryTicker.Stop()
	defer heartbeatTicker.Stop()
	defer deployTicker.Stop()
	for {
		select {
		case <-ctx.Done():
			return nil
		case <-telemetryTicker.C:
			if err := collect(); err != nil {
				return err
			}
			updated, err := client.ApplicationChecks(ctx, identity.APIKey)
			if unauthorized(err) {
				return err
			}
			if err == nil {
				checks = updated
			}
			if err := runChecks(); err != nil {
				return err
			}
		case <-heartbeatTicker.C:
			if err := heartbeat(); err != nil {
				return err
			}
		case <-deployTicker.C:
			if identity.DeploymentOptIn {
				if err := pollDeployments(ctx, client, identity); err != nil {
					slog.Warn("deployment polling failed", "error", err)
				}
			}
		}
	}
}

func pollDeployments(ctx context.Context, client *Client, identity protocol.Identity) error {
	deploys, err := client.PendingDeployments(ctx, identity.APIKey)
	if unauthorized(err) {
		return err
	}
	if err != nil {
		return err
	}
	for _, deploy := range deploys {
		result := runDeployment(ctx, deploy)
		if err := client.ReportDeploymentResult(ctx, identity.APIKey, result); err != nil {
			if unauthorized(err) {
				return err
			}
			slog.Warn("deployment result upload failed", "deploymentId", deploy.ID, "error", err)
		}
	}
	return nil
}

func unauthorized(err error) bool {
	var statusErr *HTTPStatusError
	return errors.As(err, &statusErr) && (statusErr.Code == 401 || statusErr.Code == 403)
}

func sendHeartbeat(ctx context.Context, client *Client, identity protocol.Identity, collectorHealthy bool, collectorHealth string) error {
	collectCtx, cancel := context.WithTimeout(ctx, 5*time.Second)
	defer cancel()
	_, osVersion, architecture, uptime, err := host.HostDetails(collectCtx)
	if err != nil {
		return err
	}
	return client.Heartbeat(ctx, identity.APIKey, protocol.HeartbeatRequest{
		Version: Version, OSVersion: osVersion, Architecture: architecture, UptimeSeconds: uptime,
		CollectorHealthy: collectorHealthy, CollectorHealth: collectorHealth, Capabilities: agentCapabilities(identity.DeploymentOptIn), Services: []any{},
	})
}

func clampInterval(value, fallback int) int {
	if value < 10 || value > 3600 {
		return fallback
	}
	return value
}

func platformName() string {
	switch runtime.GOOS {
	case "windows":
		return "Windows"
	case "linux":
		return "Linux"
	default:
		return runtime.GOOS
	}
}

func agentCapabilities(deploymentOptIn bool) []string {
	caps := []string{"metrics", "heartbeat", "application_checks", "process_checks"}
	if deploymentOptIn {
		caps = append(caps, "deployment_execution")
	}
	switch runtime.GOOS {
	case "windows":
		caps = append(caps, "windows_service_checks")
	case "linux":
		caps = append(caps, "systemd_service_checks")
	}
	return caps
}
