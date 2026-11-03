package agent

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"testing"
	"time"

	"github.com/support360/support360/agent/internal/protocol"
)

func TestClientRequiresHTTPSByDefault(t *testing.T) {
	if _, err := NewClient("http://localhost:8080", false); err == nil {
		t.Fatal("HTTP unexpectedly accepted")
	}
	if _, err := NewClient("http://localhost:8080", true); err != nil {
		t.Fatalf("explicit development HTTP rejected: %v", err)
	}
	if _, err := NewClient("https://support360.example", false); err != nil {
		t.Fatalf("HTTPS rejected: %v", err)
	}
	for _, rawURL := range []string{"https://user:pass@support360.example", "https://support360.example/?token=x", "https://support360.example/#fragment"} {
		if _, err := NewClient(rawURL, false); err == nil {
			t.Fatalf("unsafe server URL accepted: %s", rawURL)
		}
	}
}

func TestDeploymentCapabilityRequiresExplicitLocalOptIn(t *testing.T) {
	for _, capability := range agentCapabilities(false) {
		if capability == "deployment_execution" {
			t.Fatal("monitoring-only agent advertised deployment execution")
		}
	}
	capabilities := agentCapabilities(true)
	found := false
	for _, capability := range capabilities {
		if capability == "deployment_execution" {
			found = true
		}
	}
	if !found {
		t.Fatal("locally opted-in agent did not advertise deployment capability")
	}
}

func TestEnrollmentPersistsDeploymentOptInLocally(t *testing.T) {
	var receivedOptIn bool
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		var request protocol.EnrollRequest
		if err := json.NewDecoder(r.Body).Decode(&request); err != nil {
			http.Error(w, "invalid request", http.StatusBadRequest)
			return
		}
		for _, capability := range request.Capabilities {
			if capability == "deployment_execution" {
				receivedOptIn = true
			}
		}
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(protocol.EnrollResponse{
			AgentID: "agent-1", TenantID: "tenant-1", APIKey: "test-agent-api-key",
			HeartbeatIntervalSecond: 30, TelemetryIntervalSecond: 60,
			AllowedMetrics: []string{"cpu.usage.percent"}, ServerTime: time.Now().UTC(),
		})
	}))
	defer server.Close()

	client, err := NewClient(server.URL, true)
	if err != nil {
		t.Fatal(err)
	}
	identityPath := filepath.Join(t.TempDir(), "identity.json")
	if _, err := client.Enroll(context.Background(), "single-use-token", identityPath, true); err != nil {
		t.Fatal(err)
	}
	if !receivedOptIn {
		t.Fatal("enrollment did not advertise the explicit local deployment opt-in")
	}
	identity, err := ReadIdentity(identityPath)
	if err != nil {
		t.Fatal(err)
	}
	if !identity.DeploymentOptIn {
		t.Fatal("deployment opt-in was not persisted in the protected identity file")
	}
}

func TestHTTPApplicationCheckReportsHealthyAndFailureCategory(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.WriteHeader(http.StatusServiceUnavailable) }))
	defer server.Close()
	result := executeCheck(context.Background(), protocol.ApplicationCheck{ID: "check-1", Type: "Http", Target: server.URL})
	if result.Status != "Unhealthy" || result.ErrorCategory != "HttpStatus" {
		t.Fatalf("unexpected result: %+v", result)
	}

	server.Close()
	healthy := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.WriteHeader(http.StatusNoContent) }))
	defer healthy.Close()
	result = executeCheck(context.Background(), protocol.ApplicationCheck{ID: "check-1", Type: "Http", Target: healthy.URL})
	if result.Status != "Healthy" || result.EventID == "" {
		t.Fatalf("unexpected result: %+v", result)
	}
}

func TestHTTPApplicationCheckRejectsUnsafeTargets(t *testing.T) {
	for _, target := range []string{
		"file:///etc/passwd",
		"ftp://example.com/health",
		"https://user:pass@example.com/health",
		"/relative/health",
		"http://",
		"https://",
	} {
		result := executeCheck(context.Background(), protocol.ApplicationCheck{ID: "check-1", Type: "Http", Target: target})
		if result.Status != "Unhealthy" || result.ErrorCategory != "InvalidTarget" {
			t.Fatalf("target %q was not rejected: %+v", target, result)
		}
	}
}

func TestHTTPApplicationCheckDoesNotFollowRedirects(t *testing.T) {
	var redirected bool
	target := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		redirected = true
		w.WriteHeader(http.StatusNoContent)
	}))
	defer target.Close()
	redirector := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		http.Redirect(w, r, target.URL, http.StatusFound)
	}))
	defer redirector.Close()

	result := executeCheck(context.Background(), protocol.ApplicationCheck{ID: "check-1", Type: "Http", Target: redirector.URL})

	if result.Status != "Unhealthy" || result.ErrorCategory != "HttpStatus" || redirected {
		t.Fatalf("redirect was followed or mishandled: %+v", result)
	}
}

func TestHTTPApplicationCheckIgnoresEnvironmentProxy(t *testing.T) {
	t.Setenv("HTTP_PROXY", "http://127.0.0.1:1")
	t.Setenv("HTTPS_PROXY", "http://127.0.0.1:1")
	t.Setenv("NO_PROXY", "")
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNoContent)
	}))
	defer server.Close()

	result := executeCheck(context.Background(), protocol.ApplicationCheck{ID: "check-1", Type: "Http", Target: server.URL})

	if result.Status != "Healthy" {
		t.Fatalf("environment proxy affected the check: %+v", result)
	}
}
