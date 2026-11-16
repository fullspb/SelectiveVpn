#!/usr/bin/env bash
# Build/install selectivevpn-git from the *current working tree* (includes uncommitted files).
# Does not require pushing to GitHub.
#
# Usage:
#   packaging/aur/selectivevpn-git/makepkg-local.sh          # build package only
#   packaging/aur/selectivevpn-git/makepkg-local.sh -si       # build + install (needs sudo/pacman)
#
# If pacman lacks dotnet-sdk-10.0 / dotnet-runtime-10.0 (home ~/.dotnet SDK only),
# this script adds -d (--nodeps) automatically so makepkg can proceed.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(git -C "$HERE/../../.." rev-parse --show-toplevel 2>/dev/null || realpath "$HERE/../../..")"
BUILD_ROOT="${TMPDIR:-/tmp}/selectivevpn-aur-local-$$"

cleanup() { rm -rf "$BUILD_ROOT"; }
trap cleanup EXIT

mkdir -p "$BUILD_ROOT"
cp -a "$HERE/PKGBUILD" "$HERE/selectivevpn.install" "$BUILD_ROOT/"

python3 - <<PY
from pathlib import Path
root = Path(${ROOT@Q})
pb = Path(${BUILD_ROOT@Q}) / "PKGBUILD"
text = pb.read_text()
# Drop GitHub git source — local prepare() will rsync the working tree.
text = text.replace(
    'source=("\$_pkgname::git+https://github.com/fullspb/SelectiveVpn.git")',
    'source=()\n_local_src=' + repr(str(root)),
    1,
)
# Replace prepare() to copy working tree (tracked + untracked packaging files).
old_prepare = '''prepare() {
  cd "\$srcdir/\$_pkgname"
  [[ -f SelectiveVpn.Desktop/Helpers/apply-mihomo-caps.sh ]]
  [[ -f SelectiveVpn.Desktop/Helpers/teardown-tunnel.sh ]]
}'''
new_prepare = '''prepare() {
  mkdir -p "\$srcdir/\$_pkgname"
  # Working tree copy (exclude build junk). Uncommitted packaging files are included.
  rsync -a --delete \\
    --exclude '.git/' \\
    --exclude '**/bin/' \\
    --exclude '**/obj/' \\
    --exclude '_claude/' \\
    --exclude 'reports/' \\
    --exclude 'research_notes/' \\
    "\${_local_src%/}/" "\$srcdir/\$_pkgname/"
  cd "\$srcdir/\$_pkgname"
  [[ -f SelectiveVpn.Desktop/Helpers/apply-mihomo-caps.sh ]]
  [[ -f SelectiveVpn.Desktop/Helpers/teardown-tunnel.sh ]]
}'''
if old_prepare not in text:
    raise SystemExit('prepare() block not found for local patch')
text = text.replace(old_prepare, new_prepare, 1)
# pkgver from local tree without requiring describe tags
old_pkgver = '''pkgver() {
  cd "\$srcdir/\$_pkgname"
  local ver
  ver="\$(git describe --tags --long 2>/dev/null || true)"
  if [[ -n \$ver ]]; then
    ver="\${ver#v}"
    printf '%s' "\${ver//-/.}"
    return
  fi
  local csproj_ver
  csproj_ver="\$(grep -oP '(?<=<Version>)[^<]+' SelectiveVpn.Desktop/SelectiveVpn.Desktop.csproj | head -1)"
  [[ -z \$csproj_ver ]] && csproj_ver=0.0.0
  printf '%s.r%s.g%s' "\$csproj_ver" "\$(git rev-list --count HEAD)" "\$(git rev-parse --short HEAD)"
}'''
new_pkgver = '''pkgver() {
  # Prefer live git metadata from the original checkout (srcdir copy has no .git).
  local repo="\${_local_src}"
  local csproj_ver
  csproj_ver="\$(grep -oP '(?<=<Version>)[^<]+' "\$repo/SelectiveVpn.Desktop/SelectiveVpn.Desktop.csproj" | head -1)"
  [[ -z \$csproj_ver ]] && csproj_ver=0.0.0
  if [[ -d \$repo/.git ]]; then
    printf '%s.r%s.g%s' "\$csproj_ver" "\$(git -C "\$repo" rev-list --count HEAD)" "\$(git -C "\$repo" rev-parse --short HEAD)"
  else
    printf '%s' "\$csproj_ver"
  fi
}'''
if old_pkgver not in text:
    raise SystemExit('pkgver() block not found for local patch')
text = text.replace(old_pkgver, new_pkgver, 1)
# sha256sums was SKIP for git — with empty source keep only arch-specific mihomo sums
text = text.replace("sha256sums=('SKIP')\n", "")
pb.write_text(text)
print(f'Local working-tree source: {root}')
PY

# makedepends: allow home ~/.dotnet SDK when pacman packages are absent
EXTRA_ARGS=()
if ! pacman -Q dotnet-sdk-10.0 >/dev/null 2>&1 && ! pacman -Q dotnet-sdk >/dev/null 2>&1; then
  echo "note: pacman has no dotnet-sdk — using -d and PATH=\$HOME/.dotnet"
  EXTRA_ARGS+=(-d)
fi

export PATH="${HOME}/.dotnet:${PATH:-}"
export DOTNET_ROOT="${DOTNET_ROOT:-${HOME}/.dotnet}"

cd "$BUILD_ROOT"
echo "==> makepkg ${EXTRA_ARGS[*]-} $*  (in $BUILD_ROOT)"
makepkg "${EXTRA_ARGS[@]}" "$@"
# Copy artifacts back next to the recipe for convenience
shopt -s nullglob
for pkg in "$BUILD_ROOT"/*.pkg.tar.*; do
  cp -a "$pkg" "$HERE/"
  echo "==> package: $HERE/$(basename "$pkg")"
done
