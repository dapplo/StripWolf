# Builds the StripWolf setup with Inno Setup 6 (installs it with Chocolatey when it is missing).
#
#   ./installer/windows/build-installer.ps1 -Version 1.2.3 -PublishDir publish/windows -OutputName StripWolf-Setup
#
# The setup is written to the repository root as <OutputName>.exe.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [Parameter(Mandatory = $true)][string]$OutputName
)
$ErrorActionPreference = 'Stop'

$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) {
    choco install innosetup --no-progress -y
    if ($LASTEXITCODE -ne 0) { throw "Installing Inno Setup failed" }
}

$publish = (Resolve-Path $PublishDir).Path
$script = Join-Path $PSScriptRoot 'StripWolf.iss'
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOutputName=$OutputName" $script
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }
