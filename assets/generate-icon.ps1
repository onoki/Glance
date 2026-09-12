[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$assetDirectory = $PSScriptRoot
$repositoryRoot = Split-Path -Parent $assetDirectory
$publicDirectory = Join-Path $repositoryRoot "ui\public"
$pngPath = Join-Path $assetDirectory "icon.png"
$icoPath = Join-Path $assetDirectory "icon.ico"
$svgPath = Join-Path $assetDirectory "icon.svg"

function New-RoundedRectanglePath {
    param(
        [float] $X,
        [float] $Y,
        [float] $Width,
        [float] $Height,
        [float] $Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int] $Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    $bitmap.SetResolution(96, 96)

    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.ScaleTransform($Size / 512.0, $Size / 512.0)

        $ink = [System.Drawing.ColorTranslator]::FromHtml("#171a18")
        $paper = [System.Drawing.ColorTranslator]::FromHtml("#f7f8f2")
        $accent = [System.Drawing.ColorTranslator]::FromHtml("#20a464")

        $pagePath = New-RoundedRectanglePath -X 74 -Y 38 -Width 364 -Height 436 -Radius 36
        $pageBrush = [System.Drawing.SolidBrush]::new($paper)
        $pagePen = [System.Drawing.Pen]::new($ink, 22)
        $pagePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        try {
            $graphics.FillPath($pageBrush, $pagePath)
            $graphics.DrawPath($pagePen, $pagePath)
        }
        finally {
            $pagePen.Dispose()
            $pageBrush.Dispose()
            $pagePath.Dispose()
        }

        $boxPen = [System.Drawing.Pen]::new($ink, 17)
        $boxPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        try {
            foreach ($y in @(117, 225, 333)) {
                $boxPath = New-RoundedRectanglePath -X 116 -Y $y -Width 62 -Height 62 -Radius 9
                try {
                    $graphics.DrawPath($boxPen, $boxPath)
                }
                finally {
                    $boxPath.Dispose()
                }
            }
        }
        finally {
            $boxPen.Dispose()
        }

        $linePen = [System.Drawing.Pen]::new($ink, 18)
        $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        try {
            foreach ($y in @(148, 256, 364)) {
                $graphics.DrawLine($linePen, 218, $y, 383, $y)
            }
        }
        finally {
            $linePen.Dispose()
        }

        $checkPen = [System.Drawing.Pen]::new($accent, 20)
        $checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $checkPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        try {
            $checkPoints = @(
                [System.Drawing.PointF]::new(122, 145),
                [System.Drawing.PointF]::new(143, 165),
                [System.Drawing.PointF]::new(178, 122)
            )
            $graphics.DrawLines($checkPen, $checkPoints)
        }
        finally {
            $checkPen.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Get-IconPngBytes {
    param([int] $Size)

    $bitmap = New-IconBitmap -Size $Size
    $stream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $svgPath)) {
    throw "The source SVG was not found at $svgPath."
}

$mainBitmap = New-IconBitmap -Size 512
try {
    $mainBitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $mainBitmap.Dispose()
}

$iconFrames = foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    [pscustomobject]@{
        Size = $size
        Bytes = Get-IconPngBytes -Size $size
    }
}

$stream = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16] 0)
    $writer.Write([uint16] 1)
    $writer.Write([uint16] $iconFrames.Count)

    [uint32] $imageOffset = 6 + (16 * $iconFrames.Count)
    foreach ($frame in $iconFrames) {
        $dimension = if ($frame.Size -ge 256) { [byte] 0 } else { [byte] $frame.Size }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte] 0)
        $writer.Write([byte] 0)
        $writer.Write([uint16] 1)
        $writer.Write([uint16] 32)
        $writer.Write([uint32] $frame.Bytes.Length)
        $writer.Write($imageOffset)
        $imageOffset += [uint32] $frame.Bytes.Length
    }

    foreach ($frame in $iconFrames) {
        $writer.Write([byte[]] $frame.Bytes)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

New-Item -ItemType Directory -Force -Path $publicDirectory | Out-Null
Copy-Item -LiteralPath $svgPath -Destination (Join-Path $publicDirectory "icon.svg") -Force
Copy-Item -LiteralPath $pngPath -Destination (Join-Path $publicDirectory "icon.png") -Force
Copy-Item -LiteralPath $icoPath -Destination (Join-Path $publicDirectory "icon.ico") -Force

$validationBitmap = [System.Drawing.Bitmap]::FromFile($pngPath)
try {
    $transparentCorners = @(
        $validationBitmap.GetPixel(0, 0).A,
        $validationBitmap.GetPixel($validationBitmap.Width - 1, 0).A,
        $validationBitmap.GetPixel(0, $validationBitmap.Height - 1).A,
        $validationBitmap.GetPixel($validationBitmap.Width - 1, $validationBitmap.Height - 1).A
    )
    if ($transparentCorners | Where-Object { $_ -ne 0 }) {
        throw "Generated PNG corners are not transparent."
    }
}
finally {
    $validationBitmap.Dispose()
}

$icoBytes = [System.IO.File]::ReadAllBytes($icoPath)
$icoFrameCount = [BitConverter]::ToUInt16($icoBytes, 4)
if ($icoFrameCount -ne $iconFrames.Count) {
    throw "Generated ICO contains $icoFrameCount frames; expected $($iconFrames.Count)."
}

Write-Host "Generated a transparent 512px PNG and $icoFrameCount ICO frames from the Glance icon design."
