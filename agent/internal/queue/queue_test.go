package queue

import (
	"fmt"
	"path/filepath"
	"testing"
	"time"

	"github.com/support360/support360/agent/internal/protocol"
)

func TestQueuePersistsCapsAndAcknowledges(t *testing.T) {
	path := filepath.Join(t.TempDir(), "queue.json")
	q, err := Open(path)
	if err != nil {
		t.Fatal(err)
	}
	samples := make([]protocol.MetricSample, MaxEvents+5)
	for i := range samples {
		samples[i] = protocol.MetricSample{EventID: fmt.Sprint(i), Metric: "cpu.usage.percent", Value: 1, Unit: "percent", CapturedAt: time.Now().UTC()}
	}
	if err := q.Add(samples); err != nil {
		t.Fatal(err)
	}
	if q.Len() != MaxEvents {
		t.Fatalf("queue size = %d, want %d", q.Len(), MaxEvents)
	}
	batch := q.Batch()
	if len(batch) != MaxBatch {
		t.Fatalf("batch size = %d, want %d", len(batch), MaxBatch)
	}
	if err := q.Acknowledge(len(batch)); err != nil {
		t.Fatal(err)
	}
	q, err = Open(path)
	if err != nil {
		t.Fatal(err)
	}
	if q.Len() != MaxEvents-MaxBatch {
		t.Fatalf("reloaded queue size = %d", q.Len())
	}
}

func TestQueueRejectsInvalidAcknowledgement(t *testing.T) {
	q, err := Open(filepath.Join(t.TempDir(), "queue.json"))
	if err != nil {
		t.Fatal(err)
	}
	if err := q.Acknowledge(1); err == nil {
		t.Fatal("expected invalid acknowledgement error")
	}
}
