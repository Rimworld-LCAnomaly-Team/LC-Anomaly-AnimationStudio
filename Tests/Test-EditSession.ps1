$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$managed = (Resolve-Path (Join-Path $root '..\..')).Path

Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null } catch { }
}
$studio = [Reflection.Assembly]::LoadFrom((Resolve-Path (Join-Path $root '1.7\Assemblies\LCAnimationStudio.dll')))
$game = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'Assembly-CSharp.dll'))
$unity = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'UnityStubs.dll'))

$sessionType = $studio.GetType('LCAnimationStudio.Model.AnimationEditSession', $true)
$frameType = $studio.GetType('LCAnimationStudio.Model.EditableKeyframe', $true)
$session = [Activator]::CreateInstance($sessionType)
$sessionType.GetField('DefName').SetValue($session, 'LCAS_Smoke')
$sessionType.GetField('DurationTicks').SetValue($session, 60)
$frame = [Activator]::CreateInstance($frameType)
$frameType.GetField('Tick').SetValue($frame, 10)
$vector3 = $unity.GetType('UnityEngine.Vector3')
$frameType.GetField('Scale').SetValue($frame, $vector3.GetProperty('zero', [Reflection.BindingFlags]'Public,Static').GetValue($null))
$vector2 = $unity.GetType('UnityEngine.Vector2')
$pivot = [Activator]::CreateInstance($vector2, @([single]0.5, [single]0.5))
$rotationMode = $game.GetType('Verse.RotationMode')
$none = [Enum]::Parse($rotationMode, 'None')
$sessionType.GetMethod('UpsertKeyframe').Invoke($session, @('LCAS_TestNode', $frame, $pivot, $none)) | Out-Null
if ($sessionType.GetField('Tracks').GetValue($session).Count -ne 1) { throw 'Upsert failed.' }
if (-not $sessionType.GetMethod('Undo').Invoke($session, $null)) { throw 'Undo failed.' }
if ($sessionType.GetField('Tracks').GetValue($session).Count -ne 0) { throw 'Undo did not restore the session.' }
if (-not $sessionType.GetMethod('Redo').Invoke($session, $null)) { throw 'Redo failed.' }

$serializer = [Activator]::CreateInstance($studio.GetType('LCAnimationStudio.Serialization.AnimationXmlSerializer', $true))
$document = $serializer.GetType().GetMethod('Serialize').Invoke($serializer, @($session))
$xml = $document.ToString()
if ($xml -notmatch 'keyframeParts' -or $xml -notmatch 'workerType' -or $xml -match 'animationParts|workerClass') {
    throw 'Serializer did not emit the RimWorld 1.7 schema.'
}

$testDirectory = Join-Path $env:TEMP ('lcas-export-' + [Guid]::NewGuid().ToString('N'))
$exportPath = Join-Path $testDirectory '1.7\Defs\AnimationDefs\LCAS_Smoke.xml'
$tagType = $game.GetType('Verse.PawnRenderNodeTagDef', $true)
$tag = [Activator]::CreateInstance($tagType)
$tag.defName = 'LCAS_TestNode'
$tagDatabase = $game.GetType('Verse.DefDatabase`1', $true).MakeGenericType($tagType)
$tagDatabase.GetMethod('Add', [Type[]]@($tagType)).Invoke($null, @($tag)) | Out-Null
try {
    $serializer.ExportAtomic($session, $exportPath)
    $imported = $serializer.Import($exportPath, 'LCAS_Smoke')
    if ($imported.DefName -ne 'LCAS_Smoke' -or $imported.Tracks[0].Keyframes[0].Tick -ne 10 -or
        $imported.Tracks[0].Keyframes[0].Scale.x -ne 0) {
        throw 'Export/import changed the animation.'
    }
    foreach ($retiredVersion in @('1.5', '1.6')) {
        $retiredPath = Join-Path $testDirectory "$retiredVersion\Defs\AnimationDefs\Bad.xml"
        $blocked = $false
        try { $serializer.ExportAtomic($session, $retiredPath) }
        catch { $blocked = $true }
        if (-not $blocked -or (Test-Path -LiteralPath $retiredPath)) {
            throw "Serializer exported to a retired $retiredVersion path."
        }
    }
} finally {
    $tempBoundary = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($testDirectory).StartsWith($tempBoundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Test cleanup is outside its temporary directory.'
    }
    if (Test-Path -LiteralPath $testDirectory) { Remove-Item -LiteralPath $testDirectory -Recurse -Force }
}
Write-Output 'Edit-session CRUD, undo/redo, 1.7 export/import, and retired-path rejection passed.'
