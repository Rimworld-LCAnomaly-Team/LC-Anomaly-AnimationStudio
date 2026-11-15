$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$managed = (Resolve-Path (Join-Path $root '..\..')).Path
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null } catch { }
}
$studio = [Reflection.Assembly]::LoadFrom((Resolve-Path (Join-Path $root '1.7\Assemblies\LCAnimationStudio.dll')))
$temporaryRoot = Join-Path $env:TEMP ('lcas-project-' + [Guid]::NewGuid().ToString('N'))
$allowedDirectory = Join-Path $temporaryRoot '1.7\Defs\AnimationDefs'
New-Item -ItemType Directory -Path $allowedDirectory -Force | Out-Null
try {
    $settingsType = $studio.GetType('LCAnimationStudio.Bootstrap.AnimationStudioSettings', $true)
    $settings = [Activator]::CreateInstance($settingsType)
    $settingsType.GetField('ProjectRoot').SetValue($settings, $temporaryRoot)
    $resolverType = $studio.GetType('LCAnimationStudio.Serialization.ProjectPathResolver', $true)
    $constructorArguments = New-Object object[] 1
    $constructorArguments.SetValue($settings, 0)
    $resolver = [Activator]::CreateInstance($resolverType, $constructorArguments)
    $validPath = Join-Path $allowedDirectory 'LCAS_Test.xml'
    $authorized = $resolver.Authorize($validPath)
    if ($authorized -ne [IO.Path]::GetFullPath($validPath)) { throw 'Authorized path was changed unexpectedly.' }
    if ($resolver.Resolve('LCAS_New') -ne (Join-Path $allowedDirectory 'LCAS_New.xml')) {
        throw 'ProjectPathResolver did not default to the RimWorld 1.7 animation folder.'
    }
    foreach ($retiredVersion in @('1.5', '1.6')) {
        $blocked = $false
        $retiredPath = Join-Path $temporaryRoot "$retiredVersion\Defs\AnimationDefs\Bad.xml"
        try { $resolver.Authorize($retiredPath) | Out-Null }
        catch { $blocked = $true }
        if (-not $blocked) { throw "ProjectPathResolver allowed a retired $retiredVersion path." }
    }

    Set-Content -LiteralPath $validPath -Value '<Defs />' -Encoding UTF8
    $watcherType = $studio.GetType('LCAnimationStudio.Runtime.AnimationFileWatcher', $true)
    $watcher = [Activator]::CreateInstance($watcherType)
    try {
        $watcher.Watch($validPath)
        Set-Content -LiteralPath $validPath -Value '<Defs><!--changed--></Defs>' -Encoding UTF8
        Start-Sleep -Milliseconds 500
        $state = $watcher.Poll($false, 300)
        if ($state.ToString() -ne 'Changed') { throw 'File watcher did not report an external change.' }
        $watcher.Watch($validPath)
        Set-Content -LiteralPath $validPath -Value '<Defs><!--conflict--></Defs>' -Encoding UTF8
        Start-Sleep -Milliseconds 500
        $state = $watcher.Poll($true, 300)
        if ($state.ToString() -ne 'Conflict') { throw 'File watcher did not protect a dirty session.' }
    } finally { $watcher.Dispose() }
} finally {
    $tempBoundary = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($temporaryRoot).StartsWith($tempBoundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Test cleanup is outside its temporary directory.'
    }
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
Write-Output 'Project-path authorization and file-watcher conflict tests passed.'
