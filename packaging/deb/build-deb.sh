#!/usr/bin/env bash
# Build an amd64 .deb for Selective VPN (self-contained Avalonia + bundled mihomo).
# Usage (from repo root or this directory):
#   packaging/deb/build-deb.sh
# Output:
#   packaging/deb/dist/selectivevpn_0.1.25-1_amd64.deb
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(git -C "$HERE/../.." rev-parse --show-toplevel 2>/dev/null || realpath "$HERE/../..")"
VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/SelectiveVpn.Desktop/SelectiveVpn.Desktop.csproj" | head -1)"
[[ -n $VERSION ]] || VERSION=0.1.25
DEB_VERSION="${VERSION}-1"
PKG_NAME=selectivevpn
ARCH=amd64
RID=linux-x64
MIHOMO_VER=1.19.33
MIHOMO_ASSET="mihomo-linux-amd64-compatible-v${MIHOMO_VER}.gz"
MIHOMO_SHA256="386508bfb1c72c43b6a0eda73708ecdce82546581b0ebcb16366c53757c622b6"
MIHOMO_URL="https://github.com/MetaCubeX/mihomo/releases/download/v${MIHOMO_VER}/${MIHOMO_ASSET}"

export PATH="${HOME}/.dotnet:/usr/share/dotnet:${PATH:-}"
export DOTNET_ROOT="${DOTNET_ROOT:-${HOME}/.dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export NUGET_CERT_REVOCATION_MODE="${NUGET_CERT_REVOCATION_MODE:-offline}"

DIST="$HERE/dist"
WORK="$HERE/build"
STAGE="$WORK/stage"
PUBLISH="$WORK/publish"
DATA="$STAGE"
CONTROL_DIR="$STAGE/DEBIAN"

rm -rf "$WORK"
mkdir -p "$DIST" "$PUBLISH" "$CONTROL_DIR" \
  "$DATA/usr/bin" \
  "$DATA/usr/lib/$PKG_NAME" \
  "$DATA/usr/share/applications" \
  "$DATA/usr/share/icons/hicolor/256x256/apps" \
  "$DATA/usr/share/doc/$PKG_NAME"

echo "==> dotnet publish (self-contained $RID)"
cd "$ROOT"
attempt=1
until dotnet restore SelectiveVpn.Desktop/SelectiveVpn.Desktop.csproj -r "$RID"; do
  if (( attempt >= 4 )); then
    echo "dotnet restore failed" >&2
    exit 1
  fi
  echo "restore retry $attempt…" >&2
  sleep 5
  ((++attempt))
done

dotnet publish SelectiveVpn.Desktop/SelectiveVpn.Desktop.csproj \
  -c Release \
  -r "$RID" \
  --self-contained true \
  --no-restore \
  -o "$PUBLISH" \
  -p:PublishSingleFile=false \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:IncludeNativeLibrariesForSelfExtract=true

echo "==> stage app files"
cp -a "$PUBLISH/." "$DATA/usr/lib/$PKG_NAME/"

# Bundled mihomo (same pin as Core MihomoRelease)
CACHE_CANDIDATES=(
  "$HERE/cache/$MIHOMO_ASSET"
  "/tmp/mihomo-sha/$MIHOMO_ASSET"
  "/tmp/mihomo-sha/extract/$MIHOMO_ASSET"
  "${HOME}/.cache/yay/$MIHOMO_ASSET"
)
MIHOMO_GZ=""
for cand in "${CACHE_CANDIDATES[@]}"; do
  if [[ -f $cand ]]; then
    MIHOMO_GZ=$cand
    break
  fi
done
mkdir -p "$HERE/cache"
if [[ -z $MIHOMO_GZ ]]; then
  echo "==> download $MIHOMO_ASSET"
  curl -fsSL -o "$HERE/cache/$MIHOMO_ASSET" "$MIHOMO_URL"
  MIHOMO_GZ="$HERE/cache/$MIHOMO_ASSET"
fi
echo "$MIHOMO_SHA256  $MIHOMO_GZ" | sha256sum -c -
gzip -dc "$MIHOMO_GZ" > "$DATA/usr/lib/$PKG_NAME/mihomo"
chmod 755 "$DATA/usr/lib/$PKG_NAME/mihomo"
printf '%s\n' "$MIHOMO_VER" > "$DATA/usr/lib/$PKG_NAME/version.txt"

# Launcher
cat > "$DATA/usr/bin/$PKG_NAME" <<EOF
#!/bin/sh
exec /usr/lib/${PKG_NAME}/SelectiveVpn.Desktop "\$@"
EOF
chmod 755 "$DATA/usr/bin/$PKG_NAME"

# Desktop entry + icon
install -m644 "$ROOT/packaging/aur/selectivevpn.desktop" \
  "$DATA/usr/share/applications/${PKG_NAME}.desktop"
if command -v magick >/dev/null 2>&1; then
  magick "$ROOT/SelectiveVpn.Desktop/Assets/SelectiveVpn.ico[0]" \
    -background none -flatten PNG32:"$WORK/selectivevpn.png"
  install -m644 "$WORK/selectivevpn.png" \
    "$DATA/usr/share/icons/hicolor/256x256/apps/${PKG_NAME}.png"
fi

cat > "$DATA/usr/share/doc/$PKG_NAME/copyright" <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: SelectiveVpn
Source: https://github.com/fullspb/SelectiveVpn

Files: *
Copyright: SelectiveVpn contributors
License: unknown
Comment: Add an SPDX LICENSE to the upstream repo for proper Debian packaging.
EOF

# Control metadata
INSTALLED_SIZE="$(du -sk "$DATA/usr" | awk '{print $1}')"
cat > "$CONTROL_DIR/control" <<EOF
Package: $PKG_NAME
Version: $DEB_VERSION
Section: net
Priority: optional
Architecture: $ARCH
Maintainer: Sergey Ostanin <fullspb>
Installed-Size: $INSTALLED_SIZE
Depends: libcap2-bin, libc6, libgcc-s1, libstdc++6, libfontconfig1, libfreetype6, libice6, libsm6, libx11-6, libx11-xcb1, libxcb1, libgl1
Recommends: libicu74 | libicu72 | libicu70 | libicu67
Homepage: https://github.com/fullspb/SelectiveVpn
Description: Selective VPN (Avalonia) — AmneziaWG / Mihomo split tunneling
 Linux desktop client that routes selected apps/domains through Mihomo
 with AmneziaWG or subscription proxies. Self-contained .NET build;
 bundled mihomo receives CAP_NET_ADMIN via postinst setcap.
EOF

cat > "$CONTROL_DIR/postinst" <<'EOF'
#!/bin/sh
set -e
MIHOMO=/usr/lib/selectivevpn/mihomo
if [ -f "$MIHOMO" ] && command -v setcap >/dev/null 2>&1; then
  setcap 'cap_net_admin,cap_net_bind_service,cap_net_raw+eip' "$MIHOMO" || true
  if command -v getcap >/dev/null 2>&1; then
    getcap "$MIHOMO" || true
  fi
  echo "selectivevpn: capabilities applied to $MIHOMO"
fi
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q /usr/share/applications || true
fi
echo "Selective VPN installed. Run: selectivevpn"
echo "Settings: ~/.local/share/SelectiveVpn/"
EOF
chmod 755 "$CONTROL_DIR/postinst"

cat > "$CONTROL_DIR/prerm" <<'EOF'
#!/bin/sh
set -e
exit 0
EOF
chmod 755 "$CONTROL_DIR/prerm"

# Build .deb (ar archive) without requiring dpkg-deb on the build host.
DEB_FILE="$DIST/${PKG_NAME}_${DEB_VERSION}_${ARCH}.deb"
echo "==> assemble $DEB_FILE"
TMPDEB="$WORK/deb-root"
rm -rf "$TMPDEB"
mkdir -p "$TMPDEB"

(
  cd "$CONTROL_DIR"
  tar --owner=0 --group=0 --numeric-owner -czf "$TMPDEB/control.tar.gz" .
)
(
  cd "$DATA"
  # data tarball must not include the DEBIAN directory
  tar --owner=0 --group=0 --numeric-owner \
    --exclude='./DEBIAN' \
    -czf "$TMPDEB/data.tar.gz" \
    ./usr
)
echo '2.0' > "$TMPDEB/debian-binary"
(
  cd "$TMPDEB"
  # Order matters for dpkg.
  ar rcs "$DEB_FILE" debian-binary control.tar.gz data.tar.gz
)

ls -lh "$DEB_FILE"
echo "OK: $DEB_FILE"
