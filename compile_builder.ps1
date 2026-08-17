# Compiles HmiBuilder.exe. Lives as its own .ps1 because invoking csc.exe from Git Bash mangles
# the /nologo, /target: etc. switches into filesystem paths (MSYS path conversion turns "/nologo"
# into "C:/Program Files/Git/nologo"), which fails with CS2001.
$ErrorActionPreference = "Stop"
Set-Location "c:\Users\abbas\OneDrive\Documents\Automation\valveDemo2"

$csc  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$dll  = "C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.dll"

$out = & $csc /nologo /target:exe /out:"HmiBuilder.exe" /reference:"$dll" `
    "src\GenerateHmiLayout.cs" "src\MarineScreens.cs" 2>&1

$out | ForEach-Object { Write-Output $_ }

$errors = $out | Select-String -Pattern ": error "
if ($errors) {
    Write-Output "COMPILE_RESULT: FAILED"
    exit 1
}
Write-Output "COMPILE_RESULT: OK"
exit 0
