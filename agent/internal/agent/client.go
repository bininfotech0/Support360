package agent

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
	"time"

	"github.com/support360/support360/agent/internal/collectors/host"
	"github.com/support360/support360/agent/internal/protocol"
)

const Version = "0.2.0"

type Client struct {
	baseURL string
	http    *http.Client
}

type HTTPStatusError struct{ Code int }

func (e *HTTPStatusError) Error() string { return fmt.Sprintf("server returned HTTP %d", e.Code) }

func NewClient(rawURL string, allowHTTP bool) (*Client, error) {
	u, err := url.Parse(strings.TrimRight(rawURL, "/"))
	if err != nil || !u.IsAbs() || u.Host == "" || u.User != nil || u.RawQuery != "" || u.ForceQuery || strings.Contains(rawURL, "#") {
		return nil, errors.New("server URL must be absolute and must not contain credentials, a query, or a fragment")
	}
	if u.Scheme != "https" && !(allowHTTP && u.Scheme == "http") {
		return nil, errors.New("HTTPS is required; HTTP is allowed only with the explicit development flag")
	}
	return &Client{baseURL: strings.TrimRight(rawURL, "/"), http: &http.Client{Timeout: 20 * time.Second}}, nil
}

func (c *Client) Enroll(ctx context.Context, token, identityPath string, deploymentOptIn bool) (protocol.EnrollResponse, error) {
	hostname, osVersion, architecture, _, err := host.HostDetails(ctx)
	if err != nil {
		return protocol.EnrollResponse{}, err
	}
	capabilities := agentCapabilities(deploymentOptIn)
	req := protocol.EnrollRequest{Token: token, Hostname: hostname, Platform: platformName(), OSVersion: osVersion,
		Architecture: architecture, AgentVersion: Version, Capabilities: capabilities}
	var result protocol.EnrollResponse
	if err := c.request(ctx, http.MethodPost, "/api/agent/v1/enroll", "", req, &result, false); err != nil {
		return result, err
	}
	if result.AgentID == "" || result.TenantID == "" || result.APIKey == "" {
		return result, errors.New("enrollment response did not include an agent identity")
	}
	identity := protocol.Identity{AgentID: result.AgentID, TenantID: result.TenantID, APIKey: result.APIKey, DeploymentOptIn: deploymentOptIn}
	if err := writeIdentity(identityPath, identity); err != nil {
		return result, fmt.Errorf("save agent identity: %w", err)
	}
	return result, nil
}

func (c *Client) Config(ctx context.Context, key string) (protocol.AgentConfig, error) {
	var result protocol.AgentConfig
	err := c.request(ctx, http.MethodGet, "/api/agent/v1/config", key, nil, &result, true)
	return result, err
}

func (c *Client) Heartbeat(ctx context.Context, key string, body protocol.HeartbeatRequest) error {
	return c.request(ctx, http.MethodPost, "/api/agent/v1/heartbeat", key, body, nil, true)
}

func (c *Client) Telemetry(ctx context.Context, key string, batch []protocol.MetricSample) (protocol.TelemetryResponse, error) {
	var result protocol.TelemetryResponse
	err := c.request(ctx, http.MethodPost, "/api/agent/v1/telemetry", key,
		map[string]any{"samples": batch}, &result, true)
	return result, err
}

func (c *Client) ApplicationChecks(ctx context.Context, key string) ([]protocol.ApplicationCheck, error) {
	var result []protocol.ApplicationCheck
	err := c.request(ctx, http.MethodGet, "/api/agent/v1/application-checks", key, nil, &result, true)
	return result, err
}

func (c *Client) ApplicationCheckResults(ctx context.Context, key string, batch []protocol.ApplicationCheckResult) (protocol.ApplicationCheckResultsResponse, error) {
	var result protocol.ApplicationCheckResultsResponse
	err := c.request(ctx, http.MethodPost, "/api/agent/v1/application-checks/results", key, map[string]any{"results": batch}, &result, true)
	return result, err
}

func (c *Client) PendingDeployments(ctx context.Context, key string) ([]protocol.DeploymentRequest, error) {
	var result []protocol.DeploymentRequest
	err := c.request(ctx, http.MethodGet, "/api/agent/v1/deployments/pending", key, nil, &result, true)
	return result, err
}

func (c *Client) ReportDeploymentResult(ctx context.Context, key string, result protocol.DeploymentResult) error {
	return c.request(ctx, http.MethodPost, "/api/agent/v1/deployments/result", key, result, nil, true)
}

func (c *Client) request(ctx context.Context, method, path, key string, input, output any, retries bool) error {
	var body []byte
	if input != nil {
		var err error
		body, err = json.Marshal(input)
		if err != nil {
			return err
		}
	}
	attempts := 1
	if retries {
		attempts = 5
	}
	var last error
	for attempt := 0; attempt < attempts; attempt++ {
		var reader io.Reader
		if input != nil {
			reader = bytes.NewReader(body)
		}
		req, err := http.NewRequestWithContext(ctx, method, c.baseURL+path, reader)
		if err != nil {
			return err
		}
		req.Header.Set("Accept", "application/json")
		if input != nil {
			req.Header.Set("Content-Type", "application/json")
		}
		if key != "" {
			req.Header.Set("Authorization", "Bearer "+key)
		}
		response, err := c.http.Do(req)
		if err == nil {
			payload, readErr := io.ReadAll(io.LimitReader(response.Body, 2<<20))
			_ = response.Body.Close()
			if readErr != nil {
				err = readErr
			} else if response.StatusCode >= 200 && response.StatusCode < 300 {
				if output != nil && len(payload) > 0 {
					return json.Unmarshal(payload, output)
				}
				return nil
			} else {
				err = &HTTPStatusError{Code: response.StatusCode}
				if response.StatusCode < 500 && response.StatusCode != http.StatusTooManyRequests {
					return err
				}
			}
		}
		last = err
		if attempt+1 < attempts {
			delay := time.Duration(1<<attempt)*time.Second + time.Duration(time.Now().UnixNano()%int64(500*time.Millisecond))
			timer := time.NewTimer(delay)
			select {
			case <-ctx.Done():
				timer.Stop()
				return ctx.Err()
			case <-timer.C:
			}
		}
	}
	return last
}
