<#
    Draws the ComNotify application icon (the same serial-plug glyph the tray uses) and
    writes a multi-resolution .ico with PNG-compressed frames.

    Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 -OutFile bin\ComNotify.ico
#>
[CmdletBinding()]
param(
    [string] $OutFile
)

$ErrorActionPreference = 'Stop'
if (-not $OutFile) {
    $here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
    $OutFile = Join-Path $here '..\bin\ComNotify.ico'
}
Add-Type -AssemblyName System.Drawing

function New-PlugBitmap {
    param([int] $Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        $s = $Size / 32.0
        $body = [System.Drawing.Color]::FromArgb(0x3F, 0xC1, 0x5A)

        # cable
        $pen = New-Object System.Drawing.Pen($body, (3.2 * $s))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($pen, (2.5 * $s), (16 * $s), (11 * $s), (16 * $s))
        $pen.Dispose()

        # connector shell
        $pts = @(
            (New-Object System.Drawing.PointF((11.0 * $s), (8.5 * $s))),
            (New-Object System.Drawing.PointF((22.0 * $s), (6.0 * $s))),
            (New-Object System.Drawing.PointF((29.0 * $s), (11.0 * $s))),
            (New-Object System.Drawing.PointF((29.0 * $s), (21.0 * $s))),
            (New-Object System.Drawing.PointF((22.0 * $s), (26.0 * $s))),
            (New-Object System.Drawing.PointF((11.0 * $s), (23.5 * $s)))
        )
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddPolygon([System.Drawing.PointF[]] $pts)
        $fill = New-Object System.Drawing.SolidBrush($body)
        $g.FillPath($fill, $path)
        $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 0, 0, 0), (1.1 * $s))
        $g.DrawPath($edge, $path)
        $fill.Dispose(); $edge.Dispose(); $path.Dispose()

        # pins
        $pin = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
        $d = 3.4 * $s
        $g.FillEllipse($pin, (17.0 * $s), (11.0 * $s), $d, $d)
        $g.FillEllipse($pin, (22.5 * $s), (12.4 * $s), $d, $d)
        $g.FillEllipse($pin, (17.0 * $s), (17.2 * $s), $d, $d)
        $g.FillEllipse($pin, (22.5 * $s), (17.6 * $s), $d, $d)
        $pin.Dispose()
    }
    finally {
        $g.Dispose()
    }
    return $bmp
}

$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bmp = New-PlugBitmap -Size $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
    $bmp.Dispose()
}

$dir = Split-Path -Parent $OutFile
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$out = [System.IO.File]::Create($OutFile)
$w = New-Object System.IO.BinaryWriter($out)
try {
    $w.Write([UInt16] 0)                    # reserved
    $w.Write([UInt16] 1)                    # type: icon
    $w.Write([UInt16] $frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($f in $frames) {
        $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $w.Write([Byte] $dim)               # width
        $w.Write([Byte] $dim)               # height
        $w.Write([Byte] 0)                  # palette
        $w.Write([Byte] 0)                  # reserved
        $w.Write([UInt16] 1)                # colour planes
        $w.Write([UInt16] 32)               # bits per pixel
        $w.Write([UInt32] $f.Bytes.Length)
        $w.Write([UInt32] $offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $w.Write($f.Bytes) }
}
finally {
    $w.Flush(); $w.Dispose(); $out.Dispose()
}

Write-Host ("Wrote {0} ({1} frames, {2:N0} bytes)" -f $OutFile, $frames.Count, (Get-Item $OutFile).Length)
