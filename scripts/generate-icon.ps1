$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/ScreenRecorder/Assets'
New-Item -ItemType Directory -Force -Path $assetPath | Out-Null
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size,$size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.ScaleTransform(($size / 32.0),($size / 32.0))
        $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(33,98,213))
        $outline = New-Object System.Drawing.Pen([System.Drawing.Color]::White,2)
        $dot = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,80,80))
        try {
            $graphics.FillEllipse($background,1,1,30,30)
            $graphics.DrawRectangle($outline,6,8,20,14)
            $graphics.DrawLine($outline,11,26,21,26)
            $graphics.FillEllipse($dot,12,11,9,9)
        } finally { $background.Dispose(); $outline.Dispose(); $dot.Dispose() }
        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
            $frames += @{ Size=$size; Bytes=$stream.ToArray() }
        } finally { $stream.Dispose() }
        if ($size -eq 256) { $bitmap.Save((Join-Path $assetPath 'App.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
$file = [System.IO.File]::Create((Join-Path $assetPath 'App.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if($frame.Size -eq 256){0}else{$frame.Size}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
