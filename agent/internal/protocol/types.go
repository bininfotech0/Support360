package protocol

import "time"

type EnrollRequest struct {
	Token        string   `json:"token"`
	Hostname     string   `json:"hostname"`
	Platform     string   `json:"platform"`
	OSVersion    string   `json:"osVersion"`
	Architecture string   `json:"architecture"`
	AgentVersion string   `json:"agentVersion"`
	Capabilities []string `json:"capabilities"`
}

type EnrollResponse struct {
	AgentID                 string    `json:"agentId"`
	TenantID                string    `json:"tenantId"`
	APIKey                  string    `json:"apiKey"`
	HeartbeatIntervalSecond int       `json:"heartbeatIntervalSeconds"`
	TelemetryIntervalSecond int       `json:"telemetryIntervalSeconds"`
	AllowedMetrics          []string  `json:"allowedMetrics"`
	ServerTime              time.Time `json:"serverTime"`
}

type Identity struct {
	AgentID         string `json:"agentId"`
	TenantID        string `json:"tenantId"`
	APIKey          string `json:"apiKey"`
	DeploymentOptIn bool   `json:"deploymentOptIn,omitempty"`
}

type HeartbeatRequest struct {
	Version          string   `json:"version"`
	OSVersion        string   `json:"osVersion"`
	Architecture     string   `json:"architecture"`
	UptimeSeconds    uint64   `json:"uptimeSeconds"`
	CollectorHealthy bool     `json:"collectorHealthy"`
	CollectorHealth  string   `json:"collectorHealth"`
	Capabilities     []string `json:"capabilities"`
	Services         []any    `json:"services"`
}

type MetricSample struct {
	EventID    string    `json:"eventId"`
	Metric     string    `json:"metric"`
	Value      float64   `json:"value"`
	Unit       string    `json:"unit"`
	CapturedAt time.Time `json:"capturedAt"`
}

type TelemetryResponse struct {
	Accepted   int `json:"accepted"`
	Duplicates int `json:"duplicates"`
	Rejected   int `json:"rejected"`
}

type AgentConfig struct {
	HeartbeatIntervalSeconds int      `json:"heartbeatIntervalSeconds"`
	TelemetryIntervalSeconds int      `json:"telemetryIntervalSeconds"`
	AllowedMetrics           []string `json:"allowedMetrics"`
}

type ApplicationCheck struct {
	ID              string `json:"id"`
	Name            string `json:"name"`
	Type            string `json:"type"`
	Target          string `json:"target"`
	IntervalSeconds int    `json:"intervalSeconds"`
}
type ApplicationCheckResult struct {
	CheckID              string    `json:"checkId"`
	EventID              string    `json:"eventId"`
	Status               string    `json:"status"`
	StartedAt            time.Time `json:"startedAt"`
	FinishedAt           time.Time `json:"finishedAt"`
	DurationMilliseconds int64     `json:"durationMilliseconds"`
	ErrorCategory        string    `json:"errorCategory,omitempty"`
	ErrorDetail          string    `json:"errorDetail,omitempty"`
}
type ApplicationCheckResultsResponse struct {
	Accepted   int `json:"accepted"`
	Duplicates int `json:"duplicates"`
	Rejected   int `json:"rejected"`
}
