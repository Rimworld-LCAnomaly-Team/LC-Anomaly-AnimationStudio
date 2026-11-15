[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string[]]$AnimationPath,
    [string]$RimWorldRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [switch]$WarningsAsErrors
)

$ErrorActionPreference = 'Stop'
$script:ErrorCount = 0
$script:WarningCount = 0

function Write-Issue {
    param([string]$Severity, [string]$Path, [string]$DefName, [string]$Track, [Nullable[int]]$Tick, [string]$Message)
    $parts = @($Path)
    if ($DefName) { $parts += $DefName }
    if ($Track) { $parts += $Track }
    if ($null -ne $Tick) { $parts += "tick $Tick" }
    $location = $parts -join ' :: '
    Write-Output "${Severity}: $location - $Message"
    if ($Severity -eq 'ERROR') { $script:ErrorCount++ } else { $script:WarningCount++ }
}

function Try-ParseFiniteFloat {
    param([string]$Text, [ref]$Value)
    $number = 0.0
    $style = [Globalization.NumberStyles]::Float
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $ok = [double]::TryParse($Text.Trim(), $style, $culture, [ref]$number)
    if ($ok -and -not [double]::IsNaN($number) -and -not [double]::IsInfinity($number)) {
        $Value.Value = $number
        return $true
    }
    return $false
}

function Test-Vector3 {
    param([string]$Text, [ref]$Values)
    $parts = $Text.Trim().Trim('(', ')').Split(',')
    if ($parts.Count -ne 3) { return $false }
    $numbers = @()
    foreach ($part in $parts) {
        $number = 0.0
        if (-not (Try-ParseFiniteFloat $part ([ref]$number))) { return $false }
        $numbers += $number
    }
    $Values.Value = $numbers
    return $true
}

function Get-TagDefNames {
    param([string]$Root)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $defsRoot = Join-Path $Root '1.7\Defs'
    $files = @()
    if (Test-Path -LiteralPath $defsRoot) {
        $files += Get-ChildItem -LiteralPath $defsRoot -Recurse -File -Filter '*.xml'
    }
    $baseTags = Join-Path $RimWorldRoot 'Data/Core/Defs/PawnRenderTreeDefs/PawnRenderNodeTagDefs.xml'
    if (-not (Test-Path -LiteralPath $baseTags)) {
        throw 'Base-game render node tags were not found; pass -RimWorldRoot for the installed 1.7 game.'
    }
    $files += Get-Item -LiteralPath $baseTags
    foreach ($file in $files) {
        try {
            [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
            foreach ($node in $xml.SelectNodes('//PawnRenderNodeTagDef/defName')) {
                if ($node.InnerText.Trim()) { [void]$names.Add($node.InnerText.Trim()) }
            }
        } catch { }
    }
    return ,$names
}

function Test-AnimationFile {
    param([IO.FileInfo]$File, [Collections.Generic.HashSet[string]]$KnownTags)
    try {
        [xml]$xml = Get-Content -LiteralPath $File.FullName -Raw -Encoding UTF8
    } catch {
        Write-Issue ERROR $File.FullName $null $null $null "XML parse failed: $($_.Exception.Message)"
        return
    }

    foreach ($legacy in $xml.SelectNodes('//animationParts')) {
        Write-Issue ERROR $File.FullName $null $null $null "RimWorld 1.5 field 'animationParts' is forbidden; use 'keyframeParts'."
    }
    foreach ($legacy in $xml.SelectNodes('//workerClass')) {
        Write-Issue ERROR $File.FullName $null $null $null "RimWorld 1.5 field 'workerClass' is forbidden; use 'workerType'."
    }

    $seenDefs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($def in $xml.SelectNodes('//AnimationDef')) {
        $defNameNode = $def.SelectSingleNode('./defName')
        $defName = if ($defNameNode) { $defNameNode.InnerText.Trim() } else { '' }
        if (-not $defName) { Write-Issue ERROR $File.FullName $null $null $null 'AnimationDef requires a non-empty defName.' }
        elseif (-not $seenDefs.Add($defName)) { Write-Issue ERROR $File.FullName $defName $null $null 'defName is duplicated in this file.' }

        $duration = 0
        $durationNode = $def.SelectSingleNode('./durationTicks')
        if (-not $durationNode -or -not [int]::TryParse($durationNode.InnerText.Trim(), [ref]$duration) -or $duration -le 0) {
            Write-Issue ERROR $File.FullName $defName $null $null 'durationTicks must be an integer greater than zero.'
            $duration = -1
        }
        if (-not $def.keyframeParts -and -not $def.curveParts) {
            Write-Issue ERROR $File.FullName $defName $null $null 'AnimationDef requires keyframeParts or curveParts.'
        }

        foreach ($entry in $def.SelectNodes('./keyframeParts/li')) {
            $tagNode = $entry.SelectSingleNode('./key')
            $tag = if ($tagNode) { $tagNode.InnerText.Trim() } else { '' }
            if (-not $tag) { Write-Issue ERROR $File.FullName $defName $null $null "keyframeParts entry requires a tag Def name in 'key'." }
            elseif (-not $KnownTags.Contains($tag)) { Write-Issue ERROR $File.FullName $defName $tag $null 'PawnRenderNodeTagDef is not defined in the base game or project 1.7/Defs.' }
            $frames = @($entry.SelectNodes('./value/keyframes/li'))
            $workerType = $entry.SelectSingleNode('./value/workerType')
            if (-not $workerType -or -not $workerType.InnerText.Trim()) { Write-Issue ERROR $File.FullName $defName $tag $null 'Keyframe tracks require a workerType.' }
            if ($frames.Count -eq 0) { Write-Issue ERROR $File.FullName $defName $tag $null 'Track must contain at least one keyframe.'; continue }
            $previousTick = -1
            foreach ($frame in $frames) {
                $tick = 0
                $tickNode = $frame.SelectSingleNode('./tick')
                if (-not $tickNode -or -not [int]::TryParse($tickNode.InnerText.Trim(), [ref]$tick)) {
                    Write-Issue ERROR $File.FullName $defName $tag $null 'Keyframe tick must be an integer.'
                    continue
                }
                if ($tick -le $previousTick) { Write-Issue ERROR $File.FullName $defName $tag $tick 'Keyframe ticks must be strictly increasing and unique.' }
                if ($tick -lt 0 -or ($duration -ge 0 -and $tick -gt $duration)) { Write-Issue ERROR $File.FullName $defName $tag $tick 'Keyframe tick must be within 0..durationTicks.' }
                $previousTick = $tick
                foreach ($field in @('offset', 'scale')) {
                    $node = $frame.SelectSingleNode("./$field")
                    if ($node) {
                        $values = $null
                        if (-not (Test-Vector3 $node.InnerText ([ref]$values))) { Write-Issue ERROR $File.FullName $defName $tag $tick "$field must contain three finite numbers." }
                    }
                }
                $angleNode = $frame.SelectSingleNode('./angle')
                if ($angleNode) {
                    $angle = 0.0
                    if (-not (Try-ParseFiniteFloat $angleNode.InnerText ([ref]$angle))) { Write-Issue ERROR $File.FullName $defName $tag $tick 'angle must be a finite number.' }
                }
            }
        }
        $loopModeNode = $def.SelectSingleNode('./loopMode')
        if ($loopModeNode -and @('End', 'Clamp') -contains $loopModeNode.InnerText.Trim()) {
            Write-Issue WARNING $File.FullName $defName $null $null "loopMode=$($loopModeNode.InnerText.Trim()) does not continuously loop; verify playback intent."
        }
    }
}

$root = [IO.Path]::GetFullPath($ProjectRoot)
$retiredRoots = @('1.5', '1.6', 'Source\1.5', 'Source\1.6') | ForEach-Object {
    [IO.Path]::GetFullPath((Join-Path $root $_)).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
}
if (-not $AnimationPath) { $AnimationPath = @(Join-Path $root '1.7\Defs\AnimationDefs') }
$files = @()
foreach ($path in $AnimationPath) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (@($retiredRoots | Where-Object {
        ($resolved + [IO.Path]::DirectorySeparatorChar).StartsWith($_, [StringComparison]::OrdinalIgnoreCase)
    }).Count -gt 0) {
        Write-Issue ERROR $resolved $null $null $null 'Retired RimWorld version paths are not supported; target 1.7.'
        continue
    }
    if (Test-Path -LiteralPath $resolved -PathType Container) { $files += Get-ChildItem -LiteralPath $resolved -Recurse -File -Filter '*.xml' | Where-Object { $_.Name -notlike '*.lcas.xml' } }
    elseif (Test-Path -LiteralPath $resolved -PathType Leaf) { $files += Get-Item -LiteralPath $resolved }
    else { Write-Issue WARNING $resolved $null $null $null 'Validation target does not exist.' }
}

$knownTags = Get-TagDefNames $root
foreach ($file in $files | Sort-Object FullName -Unique) { Test-AnimationFile $file $knownTags }
Write-Output "Validated $($files.Count) file(s): $script:ErrorCount error(s), $script:WarningCount warning(s)."
if ($script:ErrorCount -gt 0 -or ($WarningsAsErrors -and $script:WarningCount -gt 0)) { exit 1 }
exit 0
