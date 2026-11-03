package host

import (
	"context"
	"fmt"
	"math"
	"runtime"
	"strings"
	"time"

	"github.com/shirou/gopsutil/v4/cpu"
	"github.com/shirou/gopsutil/v4/disk"
	"github.com/shirou/gopsutil/v4/host"
	"github.com/shirou/gopsutil/v4/mem"
	"github.com/shirou/gopsutil/v4/net"

	"github.com/support360/support360/agent/internal/protocol"
)

type Collector struct {
	cpuInterval time.Duration
}

func New() *Collector { return &Collector{cpuInterval: time.Second} }

func (c *Collector) Collect(ctx context.Context) ([]protocol.MetricSample, error) {
	captured := time.Now().UTC()
	values := make([]protocol.MetricSample, 0, 8)
	add := func(name string, value float64, unit string) error {
		if math.IsNaN(value) || math.IsInf(value, 0) || value < 0 {
			return fmt.Errorf("collector returned invalid value for %s", name)
		}
		values = append(values, protocol.MetricSample{
			EventID: newEventID(), Metric: name, Value: value, Unit: unit, CapturedAt: captured,
		})
		return nil
	}

	percent, err := cpu.PercentWithContext(ctx, c.cpuInterval, false)
	if err != nil || len(percent) == 0 {
		return nil, fmt.Errorf("collect cpu utilization: %w", err)
	}
	if err := add("cpu.usage.percent", percent[0], "percent"); err != nil {
		return nil, err
	}

	memory, err := mem.VirtualMemoryWithContext(ctx)
	if err != nil {
		return nil, fmt.Errorf("collect memory: %w", err)
	}
	if err := add("memory.used.percent", memory.UsedPercent, "percent"); err != nil {
		return nil, err
	}
	if err := add("memory.used.bytes", float64(memory.Used), "bytes"); err != nil {
		return nil, err
	}

	usage, err := disk.UsageWithContext(ctx, systemDiskPath())
	if err != nil {
		return nil, fmt.Errorf("collect system disk: %w", err)
	}
	if err := add("disk.used.percent", usage.UsedPercent, "percent"); err != nil {
		return nil, err
	}
	if err := add("disk.free.bytes", float64(usage.Free), "bytes"); err != nil {
		return nil, err
	}

	interfaces, err := net.IOCountersWithContext(ctx, false)
	if err != nil {
		return nil, fmt.Errorf("collect network counters: %w", err)
	}
	var received, sent uint64
	for _, iface := range interfaces {
		received += iface.BytesRecv
		sent += iface.BytesSent
	}
	if err := add("network.rx.bytes_total", float64(received), "bytes"); err != nil {
		return nil, err
	}
	if err := add("network.tx.bytes_total", float64(sent), "bytes"); err != nil {
		return nil, err
	}

	info, err := host.InfoWithContext(ctx)
	if err != nil {
		return nil, fmt.Errorf("collect host uptime: %w", err)
	}
	if err := add("uptime.seconds", float64(info.Uptime), "seconds"); err != nil {
		return nil, err
	}
	return values, nil
}

func HostDetails(ctx context.Context) (hostname, osVersion, architecture string, uptime uint64, err error) {
	info, err := host.InfoWithContext(ctx)
	if err != nil {
		return "", "", "", 0, err
	}
	hostname = info.Hostname
	if hostname == "" {
		hostname = "unknown-host"
	}
	osVersion = strings.TrimSpace(strings.Join([]string{info.Platform, info.PlatformVersion}, " "))
	if osVersion == "" {
		osVersion = runtime.GOOS
	}
	return hostname, osVersion, runtime.GOARCH, info.Uptime, nil
}

func systemDiskPath() string {
	if runtime.GOOS == "windows" {
		return `C:\`
	}
	return "/"
}

func newEventID() string {
	// A timestamp plus random UUID keeps IDs unique across process restarts.
	return time.Now().UTC().Format("20060102150405.000000000") + "-" + randomSuffix()
}
