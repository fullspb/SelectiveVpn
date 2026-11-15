#!/usr/bin/env bash
# Emergency cleanup matching Phase 0 spike lessons.
# POST /shutdown is 404 on mihomo 1.19.x — hard kill + delete Meta + flush ip rules 9000–9010.
# Usage: pkexec ./teardown-tunnel.sh   OR   sudo ./teardown-tunnel.sh
set -euo pipefail

ENGINE="${HOME}/.local/share/SelectiveVpn/engine/mihomo"
if [[ -x "$ENGINE" ]]; then
  pkill -9 -f "$ENGINE" 2>/dev/null || true
fi
pkill -9 -f '/SelectiveVpn/engine/mihomo' 2>/dev/null || true
pkill -9 -f 'mihomo -d' 2>/dev/null || true

ip link delete Meta 2>/dev/null || true

for pref in 9000 9001 9002 9010; do
  while ip rule del pref "$pref" 2>/dev/null; do :; done
done

resolvectl flush-caches 2>/dev/null || true
resolvectl reset-server-features 2>/dev/null || true

echo "OK: mihomo killed (best-effort), Meta deleted, ip rules 9000–9010 flushed, DNS caches flushed"
