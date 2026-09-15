param([switch]$Publish, [switch]$Test)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location $taskRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $taskDotnet build src\AiryReader\AiryReader.csproj -c Release -p:RestoreLockedMode=true
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if ($Test) {
    & $taskDotnet src\AiryReader\bin\Release\net10.0-windows\win-x64\AiryReader.dll --self-test
    if ($LASTEXITCODE -ne 0) { throw 'PDF and printing tests failed; see artifacts/test-failure.txt' }
    & $taskDotnet src\AiryReader\bin\Release\net10.0-windows\win-x64\AiryReader.dll --ui-test
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed; see artifacts/test-failure.txt' }
}
if ($Publish) {
    & $taskDotnet publish src\AiryReader\AiryReader.csproj -c Release -r win-x64 --self-contained true -o artifacts\app -p:PublishReadyToRun=true
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item README.md artifacts\app\README.md -Force
}
