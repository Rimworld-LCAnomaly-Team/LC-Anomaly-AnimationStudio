$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$studio = [Reflection.Assembly]::LoadFrom((Resolve-Path (Join-Path $root '1.6\Assemblies\LCAnimationStudio.dll')))
$encoder = $studio.GetType('LCAnimationStudio.Runtime.AnimatedGifEncoder', $true)
$compress = $encoder.GetMethod('Compress', [Reflection.BindingFlags]'NonPublic,Static')
$width = 256
$height = 256
$indices = New-Object byte[] ($width * $height)
for ($i = 0; $i -lt $indices.Length; $i++) { $indices[$i] = [byte]($i % 251) }
$arguments = New-Object object[] 1
$arguments.SetValue([byte[]]$indices, 0)
$compressed = [byte[]]$compress.Invoke($null, $arguments)
$path = Join-Path $env:TEMP ('lcas-gif-' + [Guid]::NewGuid().ToString('N') + '.gif')
try {
    $stream = [IO.File]::Create($path)
    $writer = [IO.BinaryWriter]::new($stream, [Text.Encoding]::ASCII)
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes('GIF89a'))
        $writer.Write([uint16]$width); $writer.Write([uint16]$height)
        $writer.Write([byte]0xF7); $writer.Write([byte]0); $writer.Write([byte]0)
        for ($i = 0; $i -lt 256; $i++) { $writer.Write([byte]$i); $writer.Write([byte]$i); $writer.Write([byte]$i) }
        $writer.Write([byte]0x2C)
        $writer.Write([uint16]0); $writer.Write([uint16]0); $writer.Write([uint16]$width); $writer.Write([uint16]$height); $writer.Write([byte]0)
        $writer.Write([byte]8)
        for ($offset = 0; $offset -lt $compressed.Length; $offset += 255) {
            $count = [Math]::Min(255, $compressed.Length - $offset)
            $writer.Write([byte]$count); $writer.Write($compressed, $offset, $count)
        }
        $writer.Write([byte]0); $writer.Write([byte]0x3B)
    } finally { $writer.Dispose() }
    Add-Type -AssemblyName System.Drawing
    $image = [Drawing.Image]::FromFile($path)
    try {
        if ($image.Width -ne $width -or $image.Height -ne $height) { throw 'Decoded GIF dimensions differ.' }
    } finally { $image.Dispose() }
} finally {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
Write-Output 'GIF89a/LZW smoke test passed.'
