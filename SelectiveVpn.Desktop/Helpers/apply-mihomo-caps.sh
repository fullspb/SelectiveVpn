#!/usr/bin/env bash
# One-shot elevation for SelectiveVpn Linux MVP.
# Grants TUN-related capabilities to the mihomo binary (preferred over running the UI as root).
# Usage: pkexec /path/to/apply-mihomo-caps.sh /path/to/mihomo
#    or: sudo ./apply-mihomo-caps.sh ~/.local/share/SelectiveVpn/engine/mihomo
set -euo pipefail

TARGET="${1:-}"
if [[ -z "$TARGET" ]]; then
  TARGET="${HOME}/.local/share/SelectiveVpn/engine/mihomo"
fi

if [[ ! -f "$TARGET" ]]; then
  echo "mihomo binary not found: $TARGET" >&2
  exit 1
fi

# Re-apply after every engine download/update — setcap does not survive overwrite.
setcap 'cap_net_admin,cap_net_bind_service,cap_net_raw+eip' "$TARGET"
getcap "$TARGET"
echo "OK: capabilities applied to $TARGET"
