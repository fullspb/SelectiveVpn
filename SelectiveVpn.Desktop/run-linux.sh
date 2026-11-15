#!/usr/bin/env bash
# Launch SelectiveVpn.Desktop on CachyOS / KDE Wayland+XWayland.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="${HOME}/.dotnet:${PATH}"
export DOTNET_ROOT="${HOME}/.dotnet"
export DISPLAY="${DISPLAY:-:0}"
export WAYLAND_DISPLAY="${WAYLAND_DISPLAY:-wayland-1}"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
if [[ -z "${XAUTHORITY:-}" ]]; then
  for cand in /run/user/$(id -u)/xauth_* "$HOME/.Xauthority"; do
    if [[ -f "$cand" ]]; then export XAUTHORITY="$cand"; break; fi
  done
fi
cd "$ROOT"
exec dotnet run --project SelectiveVpn.Desktop/SelectiveVpn.Desktop.csproj "$@"
