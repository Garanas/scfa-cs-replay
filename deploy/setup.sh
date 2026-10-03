#!/usr/bin/env bash
# One-time setup of a fresh Ubuntu LTS VPS for the stack in deploy/compose.yaml.
# Run as root, after you can log in with an SSH key:
#
#   curl -fsSL https://raw.githubusercontent.com/Garanas/scfa-cs-replay/deploy/production/deploy/setup.sh -o setup.sh
#   sudo bash setup.sh jip
#
# It is safe to run again. What it does:
#   - a sudo user (default "jip") that logs in with the SSH keys of root / the invoking user
#   - SSH: keys only, no root login (only once that user has a key, so you cannot lock yourself out)
#   - firewall: only SSH (rate limited), HTTP and HTTPS; automatic security updates with a
#     nightly reboot when one needs it; 2 GB swap
#   - Docker Engine + compose plugin, with log rotation
#   - a "deploy" user for GitHub Actions whose key may only run vault-deploy (deploy/deploy.sh)
#   - /opt/vault with compose.yaml and .env (fill in .env, then `docker compose up -d`)

set -euo pipefail

USER_NAME="${1:-jip}"
STACK_DIR=/opt/vault
# The released state: compose.yaml matches the images built from this branch.
RAW=https://raw.githubusercontent.com/Garanas/scfa-cs-replay/deploy/production/deploy

if [[ $EUID -ne 0 ]]; then
    echo "Run as root: sudo bash $0 [user]" >&2
    exit 1
fi

step() { echo; echo "==> $*"; }

step "Packages and security updates"
export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get upgrade -yq -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold
apt-get install -yq ca-certificates curl ufw unattended-upgrades
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
# Kernel and libc fixes only take effect after a reboot; the containers restart by themselves.
cat > /etc/apt/apt.conf.d/52auto-reboot <<'EOF'
Unattended-Upgrade::Automatic-Reboot "true";
Unattended-Upgrade::Automatic-Reboot-Time "04:30";
Unattended-Upgrade::Remove-Unused-Kernel-Packages "true";
EOF
timedatectl set-timezone Europe/Amsterdam

step "User $USER_NAME"
if ! id "$USER_NAME" &>/dev/null; then
    adduser --disabled-password --gecos "" "$USER_NAME"
fi
usermod -aG sudo "$USER_NAME"
USER_HOME=$(getent passwd "$USER_NAME" | cut -d: -f6)
install -d -m 700 -o "$USER_NAME" -g "$USER_NAME" "$USER_HOME/.ssh"
# Collect the keys you logged in with (root, or the user that ran sudo).
for keys in /root/.ssh/authorized_keys "$(getent passwd "${SUDO_USER:-root}" | cut -d: -f6)/.ssh/authorized_keys"; do
    if [[ -f "$keys" && "$keys" != "$USER_HOME/.ssh/authorized_keys" ]]; then
        cat "$keys" >> "$USER_HOME/.ssh/authorized_keys"
    fi
done
if [[ -f "$USER_HOME/.ssh/authorized_keys" ]]; then
    sort -u -o "$USER_HOME/.ssh/authorized_keys" "$USER_HOME/.ssh/authorized_keys"
    chown "$USER_NAME:$USER_NAME" "$USER_HOME/.ssh/authorized_keys"
    chmod 600 "$USER_HOME/.ssh/authorized_keys"
fi

has_password() { passwd -S "$USER_NAME" | grep -q " P "; }
# Provider images (e.g. TransIP with an SSH key) create a key-only user with passwordless sudo.
has_nopasswd_sudo() { sudo -l -U "$USER_NAME" 2>/dev/null | grep -q "NOPASSWD: ALL"; }
can_sudo() { has_password || has_nopasswd_sudo; }
if [[ -t 0 ]] && ! can_sudo; then
    step "Password for $USER_NAME (needed for sudo; SSH logins use the key)"
    passwd "$USER_NAME"
fi

step "SSH hardening"
# Only lock root out once the new user can both log in (key) and administer (sudo).
if [[ -s "$USER_HOME/.ssh/authorized_keys" ]] && can_sudo; then
    cat > /etc/ssh/sshd_config.d/10-hardening.conf <<'EOF'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
MaxAuthTries 3
LoginGraceTime 30
X11Forwarding no
EOF
    # The first match wins, so 10- beats e.g. 50-cloud-init.conf (PasswordAuthentication yes).
    sshd -t
    systemctl reload ssh 2>/dev/null || systemctl reload sshd
else
    echo "!! $USER_NAME has no SSH key or cannot sudo yet: password and root login stay enabled." >&2
    echo "!! Add a key to $USER_HOME/.ssh/authorized_keys, set a password, and run this script again." >&2
fi

step "Firewall"
# limit: refuse an address that opens 6+ connections in 30 seconds (brute force).
ufw limit OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable

step "Swap"
if ! swapon --show | grep -q .; then
    fallocate -l 2G /swapfile
    chmod 600 /swapfile
    mkswap /swapfile
    swapon /swapfile
    echo "/swapfile none swap sw 0 0" >> /etc/fstab
    sysctl -w vm.swappiness=10
    echo "vm.swappiness=10" > /etc/sysctl.d/99-swappiness.conf
fi

step "Docker"
if ! command -v docker &>/dev/null; then
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    . /etc/os-release
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
        > /etc/apt/sources.list.d/docker.list
    apt-get update -q
    apt-get install -yq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
# Container logs are kept small: the disk is not for logs.
cat > /etc/docker/daemon.json <<'EOF'
{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "3" }
}
EOF
systemctl restart docker
usermod -aG docker "$USER_NAME"

step "Deploy user for GitHub Actions"
# Logs in with a key that may only run vault-deploy (see deploy.sh). Both the script and the
# authorized_keys file are root-owned, so the user cannot change what its key is allowed to do.
if ! id deploy &>/dev/null; then
    adduser --disabled-password --gecos "" deploy
fi
usermod -aG docker deploy
curl -fsSL "$RAW/deploy.sh" -o /usr/local/bin/vault-deploy
chown root:root /usr/local/bin/vault-deploy
chmod 755 /usr/local/bin/vault-deploy
install -d -m 755 -o root -g root /home/deploy/.ssh
if [[ ! -f /home/deploy/.ssh/authorized_keys ]]; then
    install -m 644 -o root -g root /dev/null /home/deploy/.ssh/authorized_keys
fi

step "Stack in $STACK_DIR"
install -d -o "$USER_NAME" -g "$USER_NAME" "$STACK_DIR"
curl -fsSL "$RAW/compose.yaml" -o "$STACK_DIR/compose.yaml"
if [[ ! -f "$STACK_DIR/.env" ]]; then
    curl -fsSL "$RAW/.env.example" -o "$STACK_DIR/.env"
fi
chown "$USER_NAME:$USER_NAME" "$STACK_DIR/compose.yaml" "$STACK_DIR/.env"

cat <<EOF

Done. Next:
  1. Log in as $USER_NAME in a NEW terminal (keep this one open until that works):
       ssh $USER_NAME@<server>
  2. Fill in $STACK_DIR/.env (ACME_EMAIL), check that DNS for the vault host points here, then:
       cd $STACK_DIR && docker compose up -d
EOF
