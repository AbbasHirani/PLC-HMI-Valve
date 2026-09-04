# Compiles HmiBuilder.exe. Lives as its own .ps1 because invoking csc.exe from Git Bash mangles
# the /nologo, /target: etc. switches into filesystem paths (MSYS path conversion turns "/nologo"
# into "C:/Program Files/Git/nologo"), which fails with CS2001.
$ErrorActionPreference = "Stop"
Set-Location "c:\Users\abbas\OneDrive\Documents\Automation\valveDemo2"

$csc  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
# Where Siemens.Engineering.dll lives. This was a single hardcoded C:\Program Files path until
# 2026-08-31, when the project moved to a machine with TIA installed on D:\Siemens and every
# compile in this repo broke with "Metadata file could not be found". Discover it instead:
# the running TIA process is the most trustworthy source, since it is the instance we attach to.
function Find-OpennessDll {
    $roots = @()
    if ($env:VALVEDEMO_OPENNESS) { $roots += $env:VALVEDEMO_OPENNESS }
    Get-Process -Name Siemens.Automation.Portal -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $bin = Split-Path $_.Path -Parent          # ...\Portal V20\Bin
            $roots += (Join-Path (Split-Path $bin -Parent) "PublicAPI\V20")
        } catch { }
    }
    $roots += "D:\Siemens\Portal V20\PublicAPI\V20"
    $roots += "C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20"
    foreach ($r in $roots) {
        $candidate = if ($r -like "*Siemens.Engineering.dll") { $r } else { Join-Path $r "Siemens.Engineering.dll" }
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

$dll = Find-OpennessDll
if (-not $dll) {
    Write-Output "COMPILE_RESULT: FAILED (Siemens.Engineering.dll not found)"
    Write-Output "  Looked under the running TIA process, D:\Siemens and C:\Program Files\Siemens."
    Write-Output "  Set VALVEDEMO_OPENNESS to the folder holding Siemens.Engineering.dll and retry."
    exit 1
}
Write-Output "Openness DLL: $dll"

$out = & $csc /nologo /target:exe /out:"HmiBuilder.exe" /reference:"$dll" `
    "src\GenerateHmiLayout.cs" "src\MarineScreens.cs" 2>&1

$out | ForEach-Object { Write-Output $_ }

# Three checks, not one. The original only grepped for ": error " and reported OK on
# CS0016 ("Could not write to output file ... used by another process"), which csc emits with
# NO leading colon - so a locked exe compiled "successfully" while the binary stayed stale.
# That silently shipped old code into several builds on 2026-08-18.
$exeBefore = if (Test-Path "HmiBuilder.exe") { (Get-Item "HmiBuilder.exe").LastWriteTime } else { [datetime]::MinValue }

$errors = $out | Select-String -Pattern "error CS|: error "
if ($errors -or $LASTEXITCODE -ne 0) {
    Write-Output "COMPILE_RESULT: FAILED"
    exit 1
}

# Belt and braces: prove the binary was actually rewritten.
if (-not (Test-Path "HmiBuilder.exe")) {
    Write-Output "COMPILE_RESULT: FAILED (no output file)"
    exit 1
}
$newest = (Get-ChildItem "src\*.cs" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
if ((Get-Item "HmiBuilder.exe").LastWriteTime -lt $newest) {
    Write-Output "COMPILE_RESULT: FAILED (HmiBuilder.exe older than sources - is it still running?)"
    exit 1
}
Write-Output "COMPILE_RESULT: OK"
exit 0
