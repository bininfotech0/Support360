package resultqueue

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"

	"github.com/support360/support360/agent/internal/protocol"
)

const MaxEvents = 500
const MaxBatch = 100

type DiskQueue struct {
	path   string
	events []protocol.ApplicationCheckResult
}

func Open(path string) (*DiskQueue, error) {
	if path == "" {
		return nil, errors.New("application result queue path is required")
	}
	q := &DiskQueue{path: path, events: make([]protocol.ApplicationCheckResult, 0, MaxEvents)}
	data, err := os.ReadFile(path)
	if err != nil && !errors.Is(err, os.ErrNotExist) {
		return nil, err
	}
	if err == nil && len(data) > 0 {
		if err := json.Unmarshal(data, &q.events); err != nil {
			return nil, errors.New("local application result queue is invalid JSON")
		}
		if len(q.events) > MaxEvents {
			q.events = append([]protocol.ApplicationCheckResult(nil), q.events[len(q.events)-MaxEvents:]...)
		}
	}
	if err := q.persist(); err != nil {
		return nil, err
	}
	return q, nil
}

func (q *DiskQueue) Add(results []protocol.ApplicationCheckResult) error {
	q.events = append(q.events, results...)
	if len(q.events) > MaxEvents {
		q.events = append([]protocol.ApplicationCheckResult(nil), q.events[len(q.events)-MaxEvents:]...)
	}
	return q.persist()
}

func (q *DiskQueue) Batch() []protocol.ApplicationCheckResult {
	n := len(q.events)
	if n > MaxBatch {
		n = MaxBatch
	}
	return append([]protocol.ApplicationCheckResult(nil), q.events[:n]...)
}

func (q *DiskQueue) Acknowledge(count int) error {
	if count < 0 || count > len(q.events) {
		return errors.New("invalid application result acknowledgement")
	}
	q.events = append([]protocol.ApplicationCheckResult(nil), q.events[count:]...)
	return q.persist()
}

func (q *DiskQueue) Len() int { return len(q.events) }

func (q *DiskQueue) persist() error {
	if err := os.MkdirAll(filepath.Dir(q.path), 0700); err != nil {
		return err
	}
	data, err := json.Marshal(q.events)
	if err != nil {
		return err
	}
	temp := q.path + ".tmp"
	if err := os.WriteFile(temp, data, 0600); err != nil {
		return err
	}
	if err := os.Rename(temp, q.path); err != nil {
		_ = os.Remove(temp)
		return err
	}
	return nil
}
