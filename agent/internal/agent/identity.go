package agent

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"

	"github.com/support360/support360/agent/internal/protocol"
)

func writeIdentity(path string, identity protocol.Identity) error {
	if path == "" {
		return errors.New("identity file path is required")
	}
	if err := os.MkdirAll(filepath.Dir(path), 0700); err != nil {
		return err
	}
	data, err := json.MarshalIndent(identity, "", "  ")
	if err != nil {
		return err
	}
	temp := path + ".tmp"
	if err := os.WriteFile(temp, data, 0600); err != nil {
		return err
	}
	if err := os.Rename(temp, path); err != nil {
		_ = os.Remove(temp)
		return err
	}
	return nil
}

func ReadIdentity(path string) (protocol.Identity, error) {
	var identity protocol.Identity
	data, err := os.ReadFile(path)
	if err != nil {
		return identity, err
	}
	if err := json.Unmarshal(data, &identity); err != nil {
		return identity, errors.New("identity file is invalid JSON")
	}
	if identity.AgentID == "" || identity.TenantID == "" || identity.APIKey == "" {
		return identity, errors.New("identity file is incomplete")
	}
	return identity, nil
}
