param(
    [Parameter(Mandatory=$true)][string]$Address,
    [Parameter(Mandatory=$true)][string]$DriverInf,
    [Parameter(Mandatory=$true)][string]$LogPath
)
$ErrorActionPreference = 'Stop'
try {
    if (![Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator approval is required.' }
    $taskIp = $null
    if (![System.Net.IPAddress]::TryParse($Address, [ref]$taskIp)) { throw 'Invalid printer IP address.' }
    $taskInf = (Resolve-Path -LiteralPath $DriverInf).Path
    if ([IO.Path]::GetFileName($taskInf) -ne 'FFSB2PLWJ.INF') { throw 'Unexpected driver INF.' }
    & pnputil.exe /add-driver $taskInf 2>&1 | Out-File -LiteralPath $LogPath -Encoding utf8
    if ($LASTEXITCODE -notin @(0,3010)) { throw "Driver staging failed: $LASTEXITCODE" }
    if (!(Get-PrinterDriver -Name 'FF Apeos C3571' -ErrorAction SilentlyContinue)) { Add-PrinterDriver -Name 'FF Apeos C3571' }
    $taskPort = 'IP_' + $Address
    $taskExistingPort = Get-PrinterPort -Name $taskPort -ErrorAction SilentlyContinue
    if ($taskExistingPort -and $taskExistingPort.PrinterHostAddress -ne $Address) { throw 'An existing port points to a different address.' }
    if (!$taskExistingPort) { Add-PrinterPort -Name $taskPort -PrinterHostAddress $Address -PortNumber 9100 }
    $taskQueue = 'Apeos C3571 (ART EX)'
    $taskExistingQueue = Get-Printer -Name $taskQueue -ErrorAction SilentlyContinue
    if ($taskExistingQueue -and ($taskExistingQueue.DriverName -ne 'FF Apeos C3571' -or $taskExistingQueue.PortName -ne $taskPort)) { throw 'An existing queue has different settings. It was not changed.' }
    if (!$taskExistingQueue) { Add-Printer -Name $taskQueue -DriverName 'FF Apeos C3571' -PortName $taskPort }
    Get-Printer -Name $taskQueue | Format-List Name,DriverName,PortName | Out-File -LiteralPath $LogPath -Append -Encoding utf8
    'SUCCESS: Driver and printer queue are ready. No print job was sent.' | Out-File -LiteralPath $LogPath -Append -Encoding utf8
    exit 0
} catch {
    $_ | Out-String | Out-File -LiteralPath $LogPath -Append -Encoding utf8
    exit 1
}
