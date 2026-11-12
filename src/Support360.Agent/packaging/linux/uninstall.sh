#!/usr/bin/env sh
set -eu
if [ "$(id -u)" -ne 0 ]; then echo "Run this uninstaller as root." >&2; exit 1; fi
systemctl disable --now support360-agent.service 2>/dev/null || true
rm -f /etc/systemd/system/support360-agent.service /etc/support360/agent.env /usr/local/bin/support360-agent
systemctl daemon-reload
if [ "${1:-}" = "--remove-identity" ]; then
  rm -rf /var/lib/support360 /var/cache/support360 /etc/support360
  userdel support360 2>/dev/null || true
  groupdel support360 2>/dev/null || true
else
  echo "Agent stopped and binaries removed. Identity and queues remain under /var/lib/support360 and /var/cache/support360."
  echo "Pass --remove-identity to delete local credentials and queued events."
fi
