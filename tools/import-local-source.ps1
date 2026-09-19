param(
    [Parameter(Mandatory = $true)]
    [string]$FirmwarePath,

    [Parameter(Mandatory = $true)]
    [string]$AppPath,

    [switch]$IncludeCad3mf
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$FirmwarePath = (Resolve-Path $FirmwarePath).Path
$AppPath = (Resolve-Path $AppPath).Path

function Assert-Exists {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path $Path)) {
        throw "$Label not found: $Path"
    }
}

Assert-Exists (Join-Path $FirmwarePath "CMakeLists.txt") "Firmware project"
Assert-Exists (Join-Path $AppPath "StreamDeckDIY.sln") "Windows app solution"

$FirmwareDest = Join-Path $RepoRoot "firmware"
$SoftwareDest = Join-Path $RepoRoot "software"
$CadDest = Join-Path $RepoRoot "cad"

if ((Test-Path $FirmwareDest) -or (Test-Path $SoftwareDest)) {
    throw "firmware/ or software/ already exists. This importer is intentionally one-shot so it cannot overwrite a public source tree by accident."
}

New-Item -ItemType Directory -Force $FirmwareDest, $SoftwareDest, $CadDest | Out-Null

# Firmware: source, tests and build configuration only.
Copy-Item (Join-Path $FirmwarePath "CMakeLists.txt") $FirmwareDest
Copy-Item (Join-Path $FirmwarePath "pico_sdk_import.cmake") $FirmwareDest

foreach ($dir in @("src", "tests", "diagnostics", "tools", "docs", "linker")) {
    $source = Join-Path $FirmwarePath $dir
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $FirmwareDest $dir) -Recurse
    }
}

# Windows app: source projects and solution only.
Copy-Item (Join-Path $AppPath "StreamDeckDIY.sln") $SoftwareDest

foreach ($dir in @(
    "StreamDeckDIY.App",
    "StreamDeckDIY.Core",
    "StreamDeckDIY.Protocol",
    "StreamDeckDIY.Protocol.Tests",
    "StreamDeckDIY.Transport",
    "docs"
)) {
    $source = Join-Path $AppPath $dir
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $SoftwareDest $dir) -Recurse
    }
}

# Defensive cleanup in case generated folders exist inside a copied project tree.
Get-ChildItem $SoftwareDest -Directory -Recurse -Force |
    Where-Object { $_.Name -in @("bin", "obj", ".vs", "artifacts") } |
    Sort-Object FullName -Descending |
    Remove-Item -Recurse -Force

if ($IncludeCad3mf) {
    $model = Join-Path $FirmwarePath "StreamDeck_R11_CarcasaFINAL.3mf"
    if (Test-Path $model) {
        Copy-Item $model $CadDest
        Write-Host "Included CAD 3MF. Review slicer metadata before publishing." -ForegroundColor Yellow
    } else {
        Write-Warning "Requested CAD 3MF was not found: $model"
    }
}

# Final safety checks. These intentionally look for common local/private material.
$forbiddenDirectories = @(
    (Join-Path $RepoRoot "firmware\build"),
    (Join-Path $RepoRoot "firmware\build-host"),
    (Join-Path $RepoRoot "firmware\backups"),
    (Join-Path $RepoRoot "software\.vs"),
    (Join-Path $RepoRoot "software\artifacts")
)

foreach ($path in $forbiddenDirectories) {
    if (Test-Path $path) {
        throw "Unexpected generated/private directory copied: $path"
    }
}

$extensions = @(".cs", ".cpp", ".c", ".hpp", ".h", ".xaml", ".xml", ".json", ".md", ".txt", ".cmake", ".py", ".sln", ".csproj", ".pubxml", ".manifest")
$suspicious = Get-ChildItem $FirmwareDest, $SoftwareDest -File -Recurse |
    Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() } |
    Select-String -Pattern "C:\\Users\\", "github_pat_", "ghp_", "BEGIN OPENSSH PRIVATE KEY", "BEGIN RSA PRIVATE KEY" -SimpleMatch

if ($suspicious) {
    $suspicious | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw "Safety scan found local paths or credential-like content. Review before committing."
}

Write-Host ""
Write-Host "Import complete." -ForegroundColor Green
Write-Host "Next: review 'git status' and 'git diff --stat' before committing."
