package agent

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"sync/atomic"
	"testing"
	"time"

	"github.com/support360/support360/agent/internal/protocol"
)

func TestRunRetainsApplicationResultsDuringOutageAndDrainsAfterRecovery(t *testing.T) {
	var resultAttempts atomic.Int32
	accepted := make(chan int, 1)
	var server *httptest.Server
	server = httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/health":
			w.WriteHeader(http.StatusOK)
		case "/api/agent/v1/application-checks":
			_ = json.NewEncoder(w).Encode([]protocol.ApplicationCheck{{
				ID:              "check-1",
				Name:            "health",
				Type:            "Http",
				Target:          server.URL + "/health",
				IntervalSeconds: 30,
			}})
		case "/api/agent/v1/config":
			_, _ = w.Write([]byte(`{"heartbeatIntervalSeconds":10,"telemetryIntervalSeconds":10,"allowedMetrics":["cpu.usage.percent"]}`))
		case "/api/agent/v1/heartbeat":
			w.WriteHeader(http.StatusOK)
		case "/api/agent/v1/telemetry":
			_ = json.NewEncoder(w).Encode(protocol.TelemetryResponse{Accepted: 0})
		case "/api/agent/v1/application-checks/results":
			var body struct {
				Results []protocol.ApplicationCheckResult `json:"results"`
			}
			if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
				http.Error(w, "invalid results", http.StatusBadRequest)
				return
			}
			if resultAttempts.Add(1) <= 5 {
				http.Error(w, "temporarily unavailable", http.StatusServiceUnavailable)
				return
			}
			_ = json.NewEncoder(w).Encode(protocol.ApplicationCheckResultsResponse{Accepted: len(body.Results)})
			select {
			case accepted <- len(body.Results):
			default:
			}
		default:
			http.NotFound(w, r)
		}
	}))
	defer server.Close()

	client, err := NewClient(server.URL, true)
	if err != nil {
		t.Fatal(err)
	}
	queuePath := filepath.Join(t.TempDir(), "telemetry.json")
	ctx, cancel := context.WithTimeout(context.Background(), 60*time.Second)
	defer cancel()
	runDone := make(chan error, 1)
	go func() {
		runDone <- Run(ctx, client, protocol.Identity{AgentID: "agent-1", TenantID: "tenant-1", APIKey: "test-api-key"}, queuePath)
	}()

	select {
	case batchSize := <-accepted:
		if batchSize < 1 {
			t.Fatalf("recovery batch contained %d results; expected the retained application result", batchSize)
		}
	case err := <-runDone:
		t.Fatalf("agent stopped before application results recovered: %v", err)
	case <-ctx.Done():
		t.Fatal("timed out waiting for queued application results to be accepted after recovery")
	}

	if resultAttempts.Load() <= 5 {
		t.Fatalf("results server saw %d attempts; expected outage retries and a recovery upload", resultAttempts.Load())
	}
	resultQueuePath := queuePath + ".checks"
	time.Sleep(time.Second)
	data, err := os.ReadFile(resultQueuePath)
	if err != nil {
		t.Fatal(err)
	}
	var queued []protocol.ApplicationCheckResult
	if err := json.Unmarshal(data, &queued); err != nil {
		t.Fatal(err)
	}
	if len(queued) != 0 {
		var runErr error
		select {
		case runErr = <-runDone:
		default:
		}
		t.Fatalf("application result queue retained %d results after server accepted the recovery batch: %s (attempts=%d runErr=%v)", len(queued), data, resultAttempts.Load(), runErr)
	}

	cancel()
	select {
	case err := <-runDone:
		if err != nil {
			t.Fatalf("agent stopped with error after cancellation: %v", err)
		}
	case <-time.After(3 * time.Second):
		t.Fatal("agent did not stop after cancellation")
	}
}

func TestRunRetainsTelemetryDuringOutageAndDrainsAfterRecovery(t *testing.T) {
	var telemetryAttempts atomic.Int32
	accepted := make(chan int, 1)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/api/agent/v1/application-checks":
			_, _ = w.Write([]byte("[]"))
		case "/api/agent/v1/config":
			_, _ = w.Write([]byte(`{"heartbeatIntervalSeconds":10,"telemetryIntervalSeconds":10,"allowedMetrics":["cpu.usage.percent"]}`))
		case "/api/agent/v1/heartbeat":
			w.WriteHeader(http.StatusOK)
		case "/api/agent/v1/telemetry":
			var body struct {
				Samples []protocol.MetricSample `json:"samples"`
			}
			if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
				http.Error(w, "invalid telemetry", http.StatusBadRequest)
				return
			}
			if telemetryAttempts.Add(1) <= 5 {
				http.Error(w, "temporarily unavailable", http.StatusServiceUnavailable)
				return
			}
			_ = json.NewEncoder(w).Encode(protocol.TelemetryResponse{Accepted: len(body.Samples)})
			select {
			case accepted <- len(body.Samples):
			default:
			}
		default:
			http.NotFound(w, r)
		}
	}))
	defer server.Close()

	client, err := NewClient(server.URL, true)
	if err != nil {
		t.Fatal(err)
	}
	queuePath := filepath.Join(t.TempDir(), "telemetry.json")
	ctx, cancel := context.WithTimeout(context.Background(), 45*time.Second)
	defer cancel()
	runDone := make(chan error, 1)
	go func() {
		runDone <- Run(ctx, client, protocol.Identity{AgentID: "agent-1", TenantID: "tenant-1", APIKey: "test-api-key"}, queuePath)
	}()

	select {
	case batchSize := <-accepted:
		if batchSize < 2 {
			t.Fatalf("recovery batch contained %d samples; expected the retained sample plus new telemetry", batchSize)
		}
	case err := <-runDone:
		t.Fatalf("agent stopped before telemetry recovered: %v", err)
	case <-ctx.Done():
		t.Fatal("timed out waiting for queued telemetry to be accepted after recovery")
	}

	if telemetryAttempts.Load() <= 5 {
		t.Fatalf("telemetry server saw %d attempts; expected outage retries and a recovery upload", telemetryAttempts.Load())
	}
	time.Sleep(time.Second)
	data, err := os.ReadFile(queuePath)
	if err != nil {
		t.Fatal(err)
	}
	var queued []protocol.MetricSample
	if err := json.Unmarshal(data, &queued); err != nil {
		t.Fatal(err)
	}
	if len(queued) != 0 {
		var runErr error
		select {
		case runErr = <-runDone:
		default:
		}
		t.Fatalf("telemetry queue retained %d samples after server accepted the recovery batch (runErr=%v)", len(queued), runErr)
	}

	cancel()
	select {
	case err := <-runDone:
		if err != nil {
			t.Fatalf("agent stopped with error after cancellation: %v", err)
		}
	case <-time.After(3 * time.Second):
		t.Fatal("agent did not stop after cancellation")
	}
}
