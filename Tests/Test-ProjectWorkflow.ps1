$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$managed = (Resolve-Path (Join-Path $root '..\..\RimWorldWin64_Data\Managed')).Path
Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null } catch { }
}
$studio = [Reflection.Assembly]::LoadFrom((Resolve-Path (Join-Path $root '1.6\Assemblies\LCAnimationStudio.dll')))
$temporaryRoot = Join-Path $env:TEMP ('lcas-project-' + [Guid]::NewGuid().ToString('N'))
$allowedDirectory = Join-Path $temporaryRoot '1.6\Defs\AnimationDefs'
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
    $blocked = $false
    try { $resolver.Authorize((Join-Path $temporaryRoot '1.5\Defs\AnimationDefs\Bad.xml')) | Out-Null }
    catch { $blocked = $true }
    if (-not $blocked) { throw 'ProjectPathResolver allowed a RimWorld 1.5 path.' }

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
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
Write-Output 'Project-path authorization and file-watcher conflict tests passed.'
