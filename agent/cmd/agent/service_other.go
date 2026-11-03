//go:build !windows

package main

import "errors"

func runService([]string) error {
	return errors.New("Windows service mode is available only on Windows; use the systemd unit on Linux")
}
