package resultqueue

import (
	"fmt"
	"path/filepath"
	"testing"

	"github.com/support360/support360/agent/internal/protocol"
)

func TestQueuePersistsCapsAndAcknowledges(t *testing.T) {
	path := filepath.Join(t.TempDir(), "results.json")
	q, err := Open(path)
	if err != nil {
		t.Fatal(err)
	}
	results := make([]protocol.ApplicationCheckResult, MaxEvents+1)
	for i := range results {
		results[i] = protocol.ApplicationCheckResult{EventID: fmt.Sprint(i)}
	}
	if err := q.Add(results); err != nil {
		t.Fatal(err)
	}
	if q.Len() != MaxEvents {
		t.Fatalf("queue size = %d", q.Len())
	}
	batch := q.Batch()
	if len(batch) != MaxBatch {
		t.Fatalf("batch size = %d", len(batch))
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
	q, err := Open(filepath.Join(t.TempDir(), "results.json"))
	if err != nil {
		t.Fatal(err)
	}
	if err := q.Acknowledge(-1); err == nil {
		t.Fatal("expected invalid acknowledgement error")
	}
}
