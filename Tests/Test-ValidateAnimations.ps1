$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $root 'Tools\ValidateAnimations.ps1'

& $validator -ProjectRoot $root -AnimationPath (Join-Path $PSScriptRoot 'Fixtures\Valid')
if ($LASTEXITCODE -ne 0) { throw 'Valid fixture was rejected.' }

& $validator -ProjectRoot $root -AnimationPath (Join-Path $PSScriptRoot 'Fixtures\Invalid')
if ($LASTEXITCODE -eq 0) { throw 'Invalid fixture was accepted.' }

foreach ($retiredVersion in @('1.5', '1.6')) {
    & $validator -ProjectRoot $root -AnimationPath (Join-Path $root "$retiredVersion\Defs\AnimationDefs")
    if ($LASTEXITCODE -eq 0) { throw "Retired $retiredVersion validation target was accepted." }
}

Write-Output 'Validator regression tests passed.'
exit 0
