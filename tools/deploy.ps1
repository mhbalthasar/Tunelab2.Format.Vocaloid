#Requires -Version 5.0
# Deploy a built .tlx into TuneLab's extension directory (file-level install; close TuneLab first).
# Project root is the parent of this script's folder (tools\).
#
# Environment overrides (all optional):
#   VFS_TLX            explicit .tlx path        (default: newest in VFS_PACKAGE_DIR / <root>\packages)
#   VFS_PACKAGE_DIR    .tlx search directory     (default: <project root>\packages)
#   VFS_EXT_DIR        TuneLab Extensions root   (default: %APPDATA%\TuneLab\Extensions)
#   VFS_EXT_NAME       extension folder name     (default: VocaloidFormatSupport)
#   VFS_ROLLBACK_DIR   previous-install backup   (default: %APPDATA%\TuneLab\Extensions-rollback)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$name = if ($env:VFS_EXT_NAME) { $env:VFS_EXT_NAME } else { 'VocaloidFormatSupport' }

if ($env:VFS_TLX) {
    $tlx = $env:VFS_TLX
}
else {
    $pkgDir = if ($env:VFS_PACKAGE_DIR) { $env:VFS_PACKAGE_DIR } else { Join-Path $root 'packages' }
    $tlx = (Get-ChildItem -Path $pkgDir -Filter '*.tlx' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
if (-not $tlx -or -not (Test-Path $tlx)) { throw 'no .tlx found - run packtlx.ps1 first (or set VFS_TLX)' }

if (Get-Process -Name TuneLab -ErrorAction SilentlyContinue) { throw 'TuneLab is running - close it before deploying (DLLs would stay locked)' }

$base = if ($env:VFS_EXT_DIR) { $env:VFS_EXT_DIR } else { Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'TuneLab\Extensions' }
$target = Join-Path $base $name

$stage = Join-Path ([IO.Path]::GetTempPath()) ('vfs-deploy-' + [guid]::NewGuid().ToString('N'))
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($tlx, $stage)
    if (-not (Test-Path (Join-Path $stage 'manifest.json'))) { throw 'archive has no manifest.json at root - not a valid .tlx' }

    New-Item -ItemType Directory -Force -Path $base | Out-Null
    if (Test-Path $target) {
        # Rollback copies must live OUTSIDE the Extensions scan tree: TuneLab
        # treats any first-level folder containing manifest.json as a package,
        # so an in-place ".old" copy shows up as a duplicate extension.
        $rbRoot = if ($env:VFS_ROLLBACK_DIR) { $env:VFS_ROLLBACK_DIR }
                  else { Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'TuneLab\Extensions-rollback' }
        New-Item -ItemType Directory -Force -Path $rbRoot | Out-Null
        $old = Join-Path $rbRoot ("{0}.old-{1}" -f $name, (Get-Date -Format 'yyyyMMdd-HHmmss'))
        Move-Item $target $old
        Write-Host "previous install moved to: $old"
    }
    Move-Item $stage $target
    Write-Host "deployed: $target  (from $tlx)"
}
finally {
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue }
}
