# Selective VPN — Arch / CachyOS packaging

Package name: **`selectivevpn-git`** (provides `selectivevpn`).

## Quick local install

```bash
cd packaging/aur/selectivevpn-git
./makepkg-local.sh -si
```

See Project store `docs/aur-install.md` for AUR publish steps.

## Layout after install

```
/usr/bin/selectivevpn
/usr/lib/selectivevpn/SelectiveVpn.Desktop
/usr/lib/selectivevpn/mihomo          # setcap CAP_NET_ADMIN in .install
/usr/lib/selectivevpn/Helpers/*.sh
/usr/share/applications/selectivevpn.desktop
/usr/share/icons/hicolor/256x256/apps/selectivevpn.png
```

User data remains in `~/.local/share/SelectiveVpn/`.
