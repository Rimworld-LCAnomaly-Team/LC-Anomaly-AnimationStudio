$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$managed = (Resolve-Path (Join-Path $root '..\..\RimWorldWin64_Data\Managed')).Path

Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {
    try { [Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null } catch { }
}
$studio = [Reflection.Assembly]::LoadFrom((Resolve-Path (Join-Path $root '1.6\Assemblies\LCAnimationStudio.dll')))
$game = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'Assembly-CSharp.dll'))
$unity = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'UnityEngine.CoreModule.dll'))

$sessionType = $studio.GetType('LCAnimationStudio.Model.AnimationEditSession', $true)
$frameType = $studio.GetType('LCAnimationStudio.Model.EditableKeyframe', $true)
$session = [Activator]::CreateInstance($sessionType)
$sessionType.GetField('DefName').SetValue($session, 'LCAS_Smoke')
$sessionType.GetField('DurationTicks').SetValue($session, 60)
$frame = [Activator]::CreateInstance($frameType)
$frameType.GetField('Tick').SetValue($frame, 10)
$vector3 = $unity.GetType('UnityEngine.Vector3')
$frameType.GetField('Scale').SetValue($frame, $vector3.GetProperty('one', [Reflection.BindingFlags]'Public,Static').GetValue($null))
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
    throw 'Serializer did not emit the RimWorld 1.6 schema.'
}

Write-Output 'Edit-session CRUD, undo/redo, and XML smoke tests passed.'
