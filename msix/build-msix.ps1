param(
    [string]$Version = "",
    [string]$PackageIdentityName = "Fulllspb.SelectiveVPN",
    [string]$Publisher = "CN=8A6C2957-1AEA-4484-8C16-17406AAD7BDC",
    [string]$PublisherDisplayName = "Fulllspb",
    [string]$DisplayName = "Selective VPN",
    [switch]$SkipSign
)

$ErrorActionPreference = "Stop"
$msixDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $msixDir
$csproj = Join-Path $root "SelectiveVpn\SelectiveVpn.csproj"
$publish = Join-Path $msixDir "publish"
$layout = Join-Path $msixDir "layout"
$output = Join-Path $msixDir "output"
$assetsOut = Join-Path $layout "Assets"
$manifestTemplate = Join-Path $msixDir "Package.appxmanifest"
$ico = Join-Path $root "SelectiveVpn\Assets\SelectiveVpn.ico"
$redist = Join-Path $root "installer\redist"
$zipName = "mihomo-windows-amd64-compatible-v1.19.33.zip"
$zip = Join-Path $redist $zipName
$url = "https://github.com/MetaCubeX/mihomo/releases/download/v1.19.33/$zipName"
$expected = "7aaf636a4f2172dcd8ec34ac4f52797b059195e5afd6409508c963e545f8864d"
$makeAppx = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"
$signTool = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"

function Get-ProjectVersion([string]$path) {
    [xml]$xml = Get-Content -Raw -Path $path
    $node = $xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $node) { throw "Version not found in $path" }
    return [string]$node
}

function Convert-VersionToPackage([string]$semver) {
    $parts = $semver.Split('.')
    while ($parts.Count -lt 4) { $parts += '0' }
    return ($parts[0..3] -join '.')
}

function Get-IconSourceBitmap([string]$iconPath) {
    Add-Type -AssemblyName PresentationCore, WindowsBase
    $uri = [Uri]::new((Resolve-Path -LiteralPath $iconPath).Path)
    $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create(
        $uri,
        [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $frame = $decoder.Frames | Sort-Object { $_.PixelWidth * $_.PixelHeight } -Descending | Select-Object -First 1
    if (-not $frame) { throw "No frames in icon: $iconPath" }
    return $frame
}

function Write-PngFromIcon([string]$iconPath, [string]$outPath, [int]$width, [int]$height, [bool]$wideCenter = $false) {
    Add-Type -AssemblyName PresentationCore, WindowsBase, System.Drawing
    $frame = Get-IconSourceBitmap $iconPath

    $side = [Math]::Min($width, $height)
    $pad = [int]($side * 0.12)
    $draw = [Math]::Max(1, $side - (2 * $pad))
    $x = if ($wideCenter) { [int](($width - $draw) / 2) } else { $pad }
    $y = [int](($height - $draw) / 2)

    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    try {
        $bg = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(15, 23, 42))
        $dc.DrawRectangle($bg, $null, (New-Object System.Windows.Rect 0, 0, $width, $height))
        $rect = New-Object System.Windows.Rect $x, $y, $draw, $draw
        $dc.DrawImage($frame, $rect)
    }
    finally { $dc.Close() }

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($width, $height, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $dir = Split-Path -Parent $outPath
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $fs = [System.IO.File]::Open($outPath, [System.IO.FileMode]::Create)
    try { $encoder.Save($fs) }
    finally { $fs.Dispose() }
}

if (-not (Test-Path $makeAppx)) { throw "MakeAppx.exe not found: $makeAppx" }
if (-not (Test-Path $ico)) { throw "Icon not found: $ico" }

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion $csproj
}
$Version = $Version.Trim().TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+') {
    throw "Invalid version: $Version"
}
$packageVersion = Convert-VersionToPackage $Version

Write-Host "Building SelectiveVpn MSIX $packageVersion"
Write-Host "Identity: $PackageIdentityName / $Publisher"

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
New-Item -ItemType Directory -Force -Path $publish, $layout, $output, $assetsOut, $redist | Out-Null

Write-Host "Publishing self-contained SelectiveVpn (win-x64)..."
dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:PublishReadyToRun=false `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

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

Write-Host "Extracting Mihomo into publish folder..."
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

if (-not (Test-Path (Join-Path $publish "SelectiveVpn.exe"))) { throw "SelectiveVpn.exe missing after publish" }
if (-not (Test-Path (Join-Path $publish "mihomo.exe"))) { throw "mihomo.exe missing after extract" }

Write-Host "Preparing package layout..."
Copy-Item -Path (Join-Path $publish "*") -Destination $layout -Recurse -Force

Write-Host "Generating Store logo assets..."
Write-PngFromIcon $ico (Join-Path $assetsOut "StoreLogo.png") 50 50
Write-PngFromIcon $ico (Join-Path $assetsOut "Square44x44Logo.png") 44 44
Write-PngFromIcon $ico (Join-Path $assetsOut "Square150x150Logo.png") 150 150
Write-PngFromIcon $ico (Join-Path $assetsOut "Square310x310Logo.png") 310 310
Write-PngFromIcon $ico (Join-Path $assetsOut "Wide310x150Logo.png") 310 150 -wideCenter:$true
Write-PngFromIcon $ico (Join-Path $assetsOut "SplashScreen.png") 620 300 -wideCenter:$true

[xml]$manifest = Get-Content -Raw -Path $manifestTemplate
$identity = $manifest.Package.Identity
$identity.Name = $PackageIdentityName
$identity.Publisher = $Publisher
$identity.Version = $packageVersion
$identity.ProcessorArchitecture = "x64"
$manifest.Package.Properties.DisplayName = $DisplayName
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName

$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace("uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
$visual = $manifest.SelectSingleNode("//uap:VisualElements", $ns)
if ($visual) {
    $visual.SetAttribute("DisplayName", $DisplayName)
}
$tile = $manifest.SelectSingleNode("//uap:DefaultTile", $ns)
if ($tile) {
    $tile.SetAttribute("ShortName", $DisplayName)
}

$manifestPath = Join-Path $layout "AppxManifest.xml"
$manifest.Save($manifestPath)

$msixPath = Join-Path $output "SelectiveVpn-$Version-x64.msix"
if (Test-Path $msixPath) { Remove-Item -Force $msixPath }

Write-Host "Packing MSIX..."
& $makeAppx pack /d $layout /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed" }

if (-not $SkipSign) {
    if (-not (Test-Path $signTool)) { throw "signtool.exe not found: $signTool" }
    $pfx = Join-Path $output "SelectiveVpn-msix-test.pfx"
    $cer = Join-Path $output "SelectiveVpn-msix-test.cer"
    $password = "SelectiveVpn-Temp-Sign"
    $secure = ConvertTo-SecureString -String $password -Force -AsPlainText

    Write-Host "Creating temporary code-signing certificate ($Publisher)..."
    Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Publisher -and $_.FriendlyName -eq "SelectiveVpn MSIX Test" } |
        ForEach-Object { Remove-Item -Path $_.PSPath -Force }

    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Publisher `
        -KeyUsage DigitalSignature `
        -FriendlyName "SelectiveVpn MSIX Test" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @(
            "2.5.29.37={text}1.3.6.1.5.5.7.3.3",
            "2.5.29.19={text}"
        )

    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null

    Write-Host "Signing $msixPath..."
    & $signTool sign /fd SHA256 /a /f $pfx /p $password $msixPath
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
    Write-Host "Test certificate: $cer"
    Write-Host "For local sideload, install the .cer into Trusted People / Trusted Root, then: Add-AppxPackage `"$msixPath`""
}

Write-Host "Done: $msixPath"
Get-Item $msixPath | Format-List FullName, Length, LastWriteTime
