package agent

import (
	"context"
	"fmt"
	"log/slog"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"time"

	"github.com/support360/support360/agent/internal/protocol"
)

func runDeployment(ctx context.Context, deploy protocol.DeploymentRequest) protocol.DeploymentResult {
	started := time.Now().UTC()
	slog.Info("deployment started", "deploymentId", deploy.ID, "repo", deploy.GitRepoURL, "branch", deploy.GitBranch, "target", deploy.TargetPath)

	workDir := filepath.Join(cacheDir(), "deployments", deploy.ID)
	if err := os.MkdirAll(workDir, 0750); err != nil {
		return protocol.DeploymentResult{DeploymentID: deploy.ID, Status: "failed", Detail: fmt.Sprintf("create work dir: %v", err)}
	}

	if deploy.GitRepoURL != "" {
		if err := gitClone(ctx, deploy.GitRepoURL, deploy.GitBranch, workDir); err != nil {
			return protocol.DeploymentResult{DeploymentID: deploy.ID, Status: "failed", Detail: fmt.Sprintf("git clone: %v", err)}
		}
	}

	var deployErr error
	switch runtime.GOOS {
	case "windows":
		deployErr = deployIIS(workDir, deploy.TargetPath)
	case "linux":
		deployErr = deployNginx(workDir, deploy.TargetPath)
	default:
		deployErr = fmt.Errorf("deployment not supported on %s", runtime.GOOS)
	}

	finished := time.Now().UTC()
	if deployErr != nil {
		slog.Error("deployment failed", "deploymentId", deploy.ID, "error", deployErr, "duration", finished.Sub(started))
		return protocol.DeploymentResult{DeploymentID: deploy.ID, Status: "failed", Detail: deployErr.Error()}
	}

	slog.Info("deployment succeeded", "deploymentId", deploy.ID, "duration", finished.Sub(started))
	return protocol.DeploymentResult{DeploymentID: deploy.ID, Status: "succeeded", Detail: ""}
}

func gitClone(ctx context.Context, repoURL, branch, dest string) error {
	args := []string{"clone", "--depth", "1"}
	if branch != "" {
		args = append(args, "--branch", branch)
	}
	args = append(args, repoURL, dest)
	cmd := exec.CommandContext(ctx, "git", args...)
	out, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("git clone failed: %w: %s", err, strings.TrimSpace(string(out)))
	}
	return nil
}

func deployIIS(workDir, targetPath string) error {
	if _, err := exec.LookPath("appcmd"); err != nil {
		return fmt.Errorf("appcmd not found; IIS deployment requires IIS installed")
	}
	siteName := filepath.Base(targetPath)
	if siteName == "" {
		siteName = "Support360App"
	}
	if _, err := exec.Command("appcmd", "add", "site", "/name:"+siteName, "/path:"+targetPath, "/bindings:http/*:80:").CombinedOutput(); err != nil {
		if !strings.Contains(strings.ToLower(err.Error()), "already exists") {
			return fmt.Errorf("create IIS site: %w", err)
		}
	}
	if out, err := exec.Command("appcmd", "set", "site", "/site.name:"+siteName, "/[path='/'].physicalPath:"+targetPath).CombinedOutput(); err != nil {
		return fmt.Errorf("set IIS physical path: %w: %s", err, strings.TrimSpace(string(out)))
	}
	copyDeployFiles(workDir, targetPath)
	if out, err := exec.Command("appcmd", "stop", "site", "/site.name:"+siteName).CombinedOutput(); err != nil {
		slog.Warn("IIS site stop warning", "error", err, "output", strings.TrimSpace(string(out)))
	}
	if out, err := exec.Command("appcmd", "start", "site", "/site.name:"+siteName).CombinedOutput(); err != nil {
		return fmt.Errorf("start IIS site: %w: %s", err, strings.TrimSpace(string(out)))
	}
	return nil
}

func deployNginx(workDir, targetPath string) error {
	if _, err := exec.LookPath("systemctl"); err != nil {
		return fmt.Errorf("systemctl not found; nginx deployment requires systemd")
	}
	if err := os.MkdirAll(targetPath, 0755); err != nil {
		return fmt.Errorf("create target dir: %w", err)
	}
	copyDeployFiles(workDir, targetPath)
	if out, err := exec.Command("systemctl", "reload", "nginx").CombinedOutput(); err != nil {
		return fmt.Errorf("reload nginx: %w: %s", err, strings.TrimSpace(string(out)))
	}
	return nil
}

func copyDeployFiles(src, dst string) error {
	entries, err := os.ReadDir(src)
	if err != nil {
		return err
	}
	for _, entry := range entries {
		srcPath := filepath.Join(src, entry.Name())
		dstPath := filepath.Join(dst, entry.Name())
		if entry.IsDir() {
			if err := copyDeployFiles(srcPath, dstPath); err != nil {
				return err
			}
		} else {
			if err := copyFile(srcPath, dstPath); err != nil {
				return err
			}
		}
	}
	return nil
}

func copyFile(src, dst string) error {
	data, err := os.ReadFile(src)
	if err != nil {
		return err
	}
	if err := os.WriteFile(dst, data, 0644); err != nil {
		return err
	}
	return nil
}

func cacheDir() string {
	if value := os.Getenv("SUPPORT360_CACHE_DIR"); value != "" {
		return value
	}
	base, err := os.UserCacheDir()
	if err != nil {
		return ".support360-agent"
	}
	return filepath.Join(base, "support360")
}
