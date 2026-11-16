# Selective VPN — Debian / Ubuntu `.deb`

Self-contained **linux-x64** Avalonia build + bundled mihomo.

## Build

```bash
# from repo root
./packaging/deb/build-deb.sh
# → packaging/deb/dist/selectivevpn_0.1.25-1_amd64.deb
```

Requires: `dotnet` (SDK), `curl`, `gzip`, `ar`, `tar`, optional `magick` for the icon.

Does **not** require `dpkg-deb` on the build host (assembles the archive with `ar`).

## Layout after install

```
/usr/bin/selectivevpn
/usr/lib/selectivevpn/SelectiveVpn.Desktop
/usr/lib/selectivevpn/mihomo          # postinst setcap CAP_NET_*
/usr/lib/selectivevpn/Helpers/*.sh
/usr/share/applications/selectivevpn.desktop
/usr/share/icons/hicolor/256x256/apps/selectivevpn.png
```

Depends: `libcap2-bin` (+ common X11/GL/font libs). No .NET SDK/runtime package required on the VM.

User data: `~/.local/share/SelectiveVpn/`.
