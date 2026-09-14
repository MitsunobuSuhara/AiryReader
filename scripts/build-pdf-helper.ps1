param([switch]$InstallDependencies)
$ErrorActionPreference = "Stop"
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location $taskRoot
$taskPython = Join-Path $taskRoot ".tools\pdf-helper-env\Scripts\python.exe"
if (!(Test-Path $taskPython)) { throw "Create .tools/pdf-helper-env with Python 3.12 x64 first." }
if ($InstallDependencies) {
    & $taskPython -m pip install -r src/pdf-helper/requirements.lock.txt
    if ($LASTEXITCODE -ne 0) { throw "Dependency installation failed" }
}
& $taskPython -m PyInstaller --noconfirm --onedir --name airy-pdf-helper --distpath artifacts/helper --workpath artifacts/helper-build --specpath artifacts --collect-all pyhanko --collect-all pyhanko_certvalidator src/pdf-helper/pdf_helper.py *> artifacts/helper-build.log
if ($LASTEXITCODE -ne 0) { throw "Helper build failed; see artifacts/helper-build.log" }
& $taskPython src/pdf-helper/test_helper.py *> artifacts/feature-tests.log
if ($LASTEXITCODE -ne 0) { throw "Feature tests failed; see artifacts/feature-tests.log" }
