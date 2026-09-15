param()
$ErrorActionPreference = "Stop"
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location $taskRoot
if (!(Test-Path "artifacts/app/Helper/airy-pdf-helper.exe")) { throw "Run build-pdf-helper.ps1 and build.ps1 -Test -Publish first." }
$taskRelease = Join-Path $taskRoot ("artifacts/release/AiryReader-1.4.2-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Path $taskRelease -Force | Out-Null
Copy-Item -LiteralPath "artifacts/app" -Destination (Join-Path $taskRelease "app") -Recurse
$taskSetup = @"
@echo off
chcp 65001 >nul
start "" "%~dp0app\AiryReader.exe" --install
"@
$taskSetup = $taskSetup.Replace([string][char]13, [string]::Empty).Replace([string][char]10,[string][char]13+[char]10)
[IO.File]::WriteAllText((Join-Path $taskRelease "Setup.cmd"), $taskSetup, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath README.md -Destination (Join-Path $taskRelease "README.md")
$taskZip = $taskRelease + ".zip"
Compress-Archive -LiteralPath (Join-Path $taskRelease "app"), (Join-Path $taskRelease "Setup.cmd"), (Join-Path $taskRelease "README.md") -DestinationPath $taskZip
Get-FileHash -LiteralPath $taskZip -Algorithm SHA256 | Format-List
$taskZip
