# Compiles a single one-off probe/tool .cs against the Openness API.
#   .\compile_tool.ps1 ProbeSomething.cs            -> ProbeSomething.exe
#   .\compile_tool.ps1 src\Foo.cs Foo.exe           -> Foo.exe
#
# Exists because ~148 .cs files in this repo each hardcoded
# "C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.dll".
# That path stopped existing on 2026-08-31 when the project moved to a machine with TIA on
# D:\Siemens, and every one of them broke at once with "Metadata file could not be found".
# Use this instead of pasting a path into the next probe.
param(
    [Parameter(Mandatory=$true)][string]$Source,
    [string]$Out
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Find-OpennessDll {
    $roots = @()
    if ($env:VALVEDEMO_OPENNESS) { $roots += $env:VALVEDEMO_OPENNESS }
    # The running TIA process is the most trustworthy source - it is the instance we attach to.
    Get-Process -Name Siemens.Automation.Portal -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $bin = Split-Path $_.Path -Parent
            $roots += (Join-Path (Split-Path $bin -Parent) "PublicAPI\V20")
        } catch { }
    }
    $roots += "D:\Siemens\Portal V20\PublicAPI\V20"
    $roots += "C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20"
    foreach ($r in $roots) {
        $c = if ($r -like "*Siemens.Engineering.dll") { $r } else { Join-Path $r "Siemens.Engineering.dll" }
        if (Test-Path $c) { return $c }
    }
    return $null
}

$dll = Find-OpennessDll
if (-not $dll) {
    Write-Output "COMPILE_RESULT: FAILED (Siemens.Engineering.dll not found)"
    Write-Output "  Set VALVEDEMO_OPENNESS to the folder holding it, and retry."
    exit 1
}
if (-not $Out) { $Out = [System.IO.Path]::GetFileNameWithoutExtension($Source) + ".exe" }

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
Write-Output "Openness DLL: $dll"
$output = & $csc /nologo /target:exe /out:"$Out" /reference:"$dll" "$Source" 2>&1
$output | ForEach-Object { Write-Output $_ }

if (($output | Select-String -Pattern "error CS|: error ") -or $LASTEXITCODE -ne 0) {
    Write-Output "COMPILE_RESULT: FAILED"
    exit 1
}
Write-Output "COMPILE_RESULT: OK -> $Out"
