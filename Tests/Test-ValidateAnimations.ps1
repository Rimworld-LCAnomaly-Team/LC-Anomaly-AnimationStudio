$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $root 'Tools\ValidateAnimations.ps1'

& $validator -ProjectRoot $root -AnimationPath (Join-Path $PSScriptRoot 'Fixtures\Valid')
if ($LASTEXITCODE -ne 0) { throw 'Valid fixture was rejected.' }

& $validator -ProjectRoot $root -AnimationPath (Join-Path $PSScriptRoot 'Fixtures\Invalid')
if ($LASTEXITCODE -eq 0) { throw 'Invalid fixture was accepted.' }

Write-Output 'Validator regression tests passed.'
exit 0
