$ErrorActionPreference = "Stop"
$installer = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $installer
$publish = Join-Path $installer "publish"
$redist = Join-Path $installer "redist"
$zipName = "mihomo-windows-amd64-compatible-v1.19.33.zip"
$zip = Join-Path $redist $zipName
$url = "https://github.com/MetaCubeX/mihomo/releases/download/v1.19.33/$zipName"
$expected = "7aaf636a4f2172dcd8ec34ac4f52797b059195e5afd6409508c963e545f8864d"

Write-Host "Publishing self-contained SelectiveVpn..."
dotnet publish (Join-Path $root "SelectiveVpn\SelectiveVpn.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

New-Item -ItemType Directory -Force -Path $redist | Out-Null
$haveZip = $false
if (Test-Path $zip) {
    $haveZip = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expected
}
if (-not $haveZip) {
    Write-Host "Downloading Mihomo 1.19.33..."
    Invoke-WebRequest -Uri $url -OutFile $zip
}
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $expected) { throw "Mihomo checksum mismatch: $hash" }

Write-Host "Extracting Mihomo next to the app..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $extracted = $false
    foreach ($entry in $archive.Entries) {
        if ([string]::IsNullOrEmpty($entry.Name)) { continue }
        $normalized = $entry.FullName.Replace("\", "/")
        if ($normalized.Contains("..") -or [System.IO.Path]::IsPathRooted($entry.FullName)) {
            throw "Unsafe path in Mihomo archive: $($entry.FullName)"
        }
        $isEngine = $entry.Name.EndsWith(".exe", [StringComparison]::OrdinalIgnoreCase) -or
            $entry.Name.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase)
        if (-not $isEngine) { continue }
        $targetName = $entry.Name
        if ($entry.Name.EndsWith(".exe", [StringComparison]::OrdinalIgnoreCase)) {
            $targetName = "mihomo.exe"
        }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $publish $targetName), $true)
        $extracted = $true
        Write-Host "  $targetName"
    }
    if (-not $extracted) { throw "Mihomo archive has no executable" }
}
finally {
    $archive.Dispose()
}

if (-not (Test-Path (Join-Path $publish "mihomo.exe"))) { throw "mihomo.exe was not extracted" }
if (-not (Test-Path (Join-Path $publish "SelectiveVpn.exe"))) { throw "SelectiveVpn.exe was not published" }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup compiler ISCC.exe was not found" }

Write-Host "Compiling installer..."
& $iscc (Join-Path $installer "SelectiveVpn.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Write-Host "Done: $(Join-Path $installer 'output\SelectiveVpn-Setup-0.1.7.exe')"
