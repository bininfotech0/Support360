package agent

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"fmt"
	"net"
	"net/http"
	"net/url"
	"os/exec"
	"runtime"
	"strings"
	"time"

	"github.com/shirou/gopsutil/v4/process"
	"github.com/support360/support360/agent/internal/protocol"
)

var httpCheckClient = &http.Client{
	Timeout: 10 * time.Second,
	Transport: &http.Transport{
		Proxy:                 nil,
		DialContext:           (&net.Dialer{Timeout: 5 * time.Second, KeepAlive: 30 * time.Second}).DialContext,
		TLSHandshakeTimeout:   5 * time.Second,
		ResponseHeaderTimeout: 10 * time.Second,
		MaxIdleConns:          4,
		IdleConnTimeout:       30 * time.Second,
		DisableCompression:    true,
	},
	CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse },
}

func validateHTTPCheckTarget(raw string) error {
	if len(raw) > 2048 {
		return fmt.Errorf("target exceeds 2048 characters")
	}
	parsed, err := url.Parse(raw)
	if err != nil || !parsed.IsAbs() || parsed.Host == "" || parsed.User != nil ||
		(!strings.EqualFold(parsed.Scheme, "http") && !strings.EqualFold(parsed.Scheme, "https")) {
		return fmt.Errorf("target must be an absolute HTTP(S) URL without embedded credentials")
	}
	return nil
}

func executeCheck(ctx context.Context, check protocol.ApplicationCheck) protocol.ApplicationCheckResult {
	started := time.Now().UTC()
	status, category, detail := "Unhealthy", "CheckFailed", "check did not succeed"
	switch strings.ToLower(check.Type) {
	case "http":
		if err := validateHTTPCheckTarget(check.Target); err != nil {
			category, detail = "InvalidTarget", err.Error()
			break
		}
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, check.Target, nil)
		if err != nil {
			category, detail = "InvalidTarget", err.Error()
			break
		}
		resp, err := httpCheckClient.Do(req)
		if err != nil {
			category, detail = "ConnectionFailed", err.Error()
			break
		}
		_ = resp.Body.Close()
		if resp.StatusCode >= 200 && resp.StatusCode < 300 {
			status, category, detail = "Healthy", "", ""
		} else {
			category, detail = "HttpStatus", resp.Status
		}
	case "process":
		procs, err := process.ProcessesWithContext(ctx)
		if err != nil {
			category, detail = "ProcessQueryFailed", err.Error()
			break
		}
		for _, p := range procs {
			name, err := p.NameWithContext(ctx)
			if err == nil && strings.EqualFold(strings.TrimSuffix(name, ".exe"), strings.TrimSuffix(check.Target, ".exe")) {
				status, category, detail = "Present", "", ""
				break
			}
		}
	case "systemdservice":
		if runtime.GOOS != "linux" {
			status, category, detail = "Unsupported", "PlatformUnsupported", "systemd checks require Linux"
			break
		}
		if _, err := exec.LookPath("systemctl"); err != nil {
			status, category, detail = "Unsupported", "CheckDependencyMissing", "systemctl is not installed on this host"
			break
		}
		if strings.ContainsAny(check.Target, " /\\\t\r\n") {
			category, detail = "InvalidTarget", "invalid service name"
			break
		}
		out, err := exec.CommandContext(ctx, "systemctl", "show", "--property=ActiveState", "--value", check.Target).CombinedOutput()
		if err != nil {
			category, detail = "ServiceQueryFailed", strings.TrimSpace(string(out))
			break
		}
		if strings.TrimSpace(string(out)) == "active" {
			status, category, detail = "Healthy", "", ""
		} else {
			category, detail = "ServiceInactive", strings.TrimSpace(string(out))
		}
	case "windowsservice":
		serviceState, err := queryWindowsService(ctx, check.Target)
		if err != nil {
			category, detail = "ServiceQueryFailed", err.Error()
		} else if serviceState == "running" {
			status, category, detail = "Healthy", "", ""
		} else if serviceState == "unsupported" {
			status, category, detail = "Unsupported", "PlatformUnsupported", "Windows service checks require Windows"
		} else {
			category, detail = "ServiceNotRunning", serviceState
		}
	default:
		status, category, detail = "Unsupported", "UnsupportedCheckType", "unsupported check type"
	}
	finished := time.Now().UTC()
	var id [16]byte
	eventID := ""
	if _, err := rand.Read(id[:]); err != nil {
		eventID = fmt.Sprintf("fallback-%d", finished.UnixNano())
	} else {
		eventID = hex.EncodeToString(id[:])
	}
	return protocol.ApplicationCheckResult{CheckID: check.ID, EventID: eventID, Status: status, StartedAt: started, FinishedAt: finished, DurationMilliseconds: finished.Sub(started).Milliseconds(), ErrorCategory: category, ErrorDetail: detail}
}
