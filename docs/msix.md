# MSIX package (Microsoft Store)

Build a Store-ready MSIX (self-contained win-x64 + bundled Mihomo):

```powershell
powershell -ExecutionPolicy Bypass -File msix\build-msix.ps1
```

Defaults match Partner Center product **Selective VPN**:

| Field | Value |
|-------|--------|
| Identity Name | `Fulllspb.SelectiveVPN` |
| Publisher | `CN=8A6C2957-1AEA-4484-8C16-17406AAD7BDC` |
| PublisherDisplayName | `Fulllspb` |
| DisplayName | `Selective VPN` |

Output:

- `msix\output\SelectiveVpn-<version>-x64.msix` — package to upload
- `msix\output\SelectiveVpn-msix-test.cer` / `.pfx` — temporary local signing only

Override if Partner Center identity changes:

```powershell
powershell -ExecutionPolicy Bypass -File msix\build-msix.ps1 `
  -PackageIdentityName "Fulllspb.SelectiveVPN" `
  -Publisher "CN=8A6C2957-1AEA-4484-8C16-17406AAD7BDC" `
  -PublisherDisplayName "Fulllspb" `
  -DisplayName "Selective VPN"
```

## Restricted capabilities

The package declares:

- `runFullTrust` — classic WPF + child `mihomo.exe`
- `allowElevation` — UAC when creating the TUN adapter

Partner Center shows a **warning** until Microsoft grants approval for these capabilities in the submission. That is expected; identity/name errors are separate and must be fixed in the manifest.

## Notes

- Architecture: **x64** only for this pipeline.
- Package version is four-part (`0.1.10.0`) derived from the app `Version`.
- After Store publication, prefer Store updates over the in-app GitHub installer flow.
