#!/usr/bin/env sh
set -eu

if [ "$(id -u)" -ne 0 ]; then echo "Run this installer as root." >&2; exit 1; fi
if [ "$#" -lt 2 ] || [ "$#" -gt 3 ]; then echo "Usage: install.sh /path/to/support360-agent https://support360.example [--enable-deployments]" >&2; exit 2; fi
binary=$(readlink -f "$1")
server=$2
deployment_flag=""
if [ "$#" -eq 3 ]; then
  [ "$3" = "--enable-deployments" ] || { echo "Unknown installer option: $3" >&2; exit 2; }
  deployment_flag=--enable-deployments
fi
case "$server" in https://*) ;; *) echo "The server URL must use HTTPS." >&2; exit 2 ;; esac
[ -x "$binary" ] || { echo "Agent binary is not executable." >&2; exit 2; }
command -v systemctl >/dev/null || { echo "systemd is required." >&2; exit 2; }

manifest_key_source=${SUPPORT360_INSTALL_MANIFEST_PUBLIC_KEY:-}
deploy_root=${SUPPORT360_INSTALL_DEPLOY_ROOT:-/var/lib/support360/deployments}
if [ -n "$deployment_flag" ]; then
  [ -n "$manifest_key_source" ] || { echo "Set SUPPORT360_INSTALL_MANIFEST_PUBLIC_KEY to the server manifest public key PEM when enabling deployments." >&2; exit 2; }
  [ -f "$manifest_key_source" ] || { echo "Manifest public key was not found." >&2; exit 2; }
  case "$deploy_root" in /*) ;; *) echo "SUPPORT360_INSTALL_DEPLOY_ROOT must be an absolute path." >&2; exit 2 ;; esac
fi

printf 'Single-use enrollment token: '
stty -echo
trap 'stty echo 2>/dev/null || true' EXIT HUP INT TERM
IFS= read -r token
stty echo
trap - EXIT HUP INT TERM
printf '\n'
[ -n "$token" ] || { echo "Enrollment token cannot be empty." >&2; exit 2; }

if ! getent group support360 >/dev/null; then groupadd --system support360; fi
if ! id support360 >/dev/null 2>&1; then useradd --system --gid support360 --home-dir /var/lib/support360 --shell /usr/sbin/nologin support360; fi
install -d -o root -g root -m 0755 /usr/local/bin
install -d -o support360 -g support360 -m 0700 /var/lib/support360 /var/cache/support360
if [ -n "$deployment_flag" ]; then install -d -o support360 -g support360 -m 0700 "$deploy_root"; fi
install -o root -g root -m 0755 "$binary" /usr/local/bin/support360-agent
install -d -o root -g support360 -m 0750 /etc/support360
cat > /etc/support360/agent.env <<EOF
SUPPORT360_SERVER_URL=$server
SUPPORT360_IDENTITY_FILE=/var/lib/support360/identity.json
SUPPORT360_QUEUE_FILE=/var/cache/support360/queue.json
EOF
if [ -n "$deployment_flag" ]; then
  install -o root -g support360 -m 0640 "$manifest_key_source" /etc/support360/manifest-public.pem
  cat >> /etc/support360/agent.env <<EOF
SUPPORT360_MANIFEST_PUBLIC_KEY=/etc/support360/manifest-public.pem
SUPPORT360_DEPLOY_ROOT=$deploy_root
EOF
fi
chown root:support360 /etc/support360/agent.env
chmod 0640 /etc/support360/agent.env

runuser -u support360 -- env SUPPORT360_ENROLLMENT_TOKEN="$token" SUPPORT360_SERVER_URL="$server" \
  /usr/local/bin/support360-agent enroll --identity /var/lib/support360/identity.json $deployment_flag
unset token
install -o root -g root -m 0644 "$(dirname "$0")/support360-agent.service" /etc/systemd/system/support360-agent.service
systemctl daemon-reload
systemctl enable --now support360-agent.service
echo "Support360 agent installed and started. Check with: systemctl status support360-agent"
