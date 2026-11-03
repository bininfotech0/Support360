//go:build !windows

package agent

import "context"

func queryWindowsService(ctx context.Context, _ string) (string, error) {
	if err := ctx.Err(); err != nil {
		return "", err
	}
	return "unsupported", nil
}
