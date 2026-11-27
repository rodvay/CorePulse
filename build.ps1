$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Add-Type -AssemblyName System.Drawing

function New-CorePulseIcon {
    param([string]$OutPath)
    $sizes = @(16, 32, 48, 256)
    $pngList = New-Object System.Collections.Generic.List[byte[]]

    foreach ($sz in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $pad = [float]($sz * 0.06)
        $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($sz - $pad * 2), ($sz - $pad * 2))
        $rad = [float]($sz * 0.24)
        $d = $rad * 2.0

        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.Color]::FromArgb(255, 16, 28, 42),
            [System.Drawing.Color]::FromArgb(255, 9, 13, 20),
            45.0
        )
        $g.FillPath($bgBrush, $path)

        $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 56, 189, 248), [Math]::Max(1.2, $sz * 0.045))
        $g.DrawPath($borderPen, $path)

        # Heartbeat / Thermal Pulse Line (Emerald)
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(($sz * 0.16), ($sz * 0.56))),
            (New-Object System.Drawing.PointF(($sz * 0.34), ($sz * 0.56))),
            (New-Object System.Drawing.PointF(($sz * 0.44), ($sz * 0.22))),
            (New-Object System.Drawing.PointF(($sz * 0.56), ($sz * 0.78))),
            (New-Object System.Drawing.PointF(($sz * 0.68), ($sz * 0.50))),
            (New-Object System.Drawing.PointF(($sz * 0.84), ($sz * 0.50)))
        )
        $pulsePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 52, 211, 153), [Math]::Max(1.6, $sz * 0.085))
        $pulsePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $pulsePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pulsePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLines($pulsePen, $pts)

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngList.Add($ms.ToArray())

        $ms.Dispose(); $pulsePen.Dispose(); $borderPen.Dispose(); $bgBrush.Dispose(); $path.Dispose(); $g.Dispose(); $bmp.Dispose()
    }

    $fs = [System.IO.File]::Create($OutPath)
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
        $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
        $offset = 6 + (16 * $sizes.Count)
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $sz = $sizes[$i]; $bytes = $pngList[$i]
            $dim = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
            $bw.Write($dim); $bw.Write($dim); $bw.Write([byte]0); $bw.Write([byte]0)
            $bw.Write([uint16]1); $bw.Write([uint16]32)
            $bw.Write([uint32]$bytes.Length); $bw.Write([uint32]$offset)
            $offset += $bytes.Length
        }
        foreach ($bytes in $pngList) { $bw.Write($bytes) }
    } finally { $bw.Dispose(); $fs.Dispose() }
}

Write-Host "Generating app.ico..."
New-CorePulseIcon -OutPath (Join-Path $scriptDir "app.ico")

$libDir = Join-Path $scriptDir "lib"
$lhmDll = Join-Path $libDir "LibreHardwareMonitorLib.dll"
if (-not (Test-Path $lhmDll)) {
    Write-Host "Downloading official LibreHardwareMonitor v0.9.6 runtime DLLs..."
    New-Item -ItemType Directory -Path $libDir -Force | Out-Null
    $tmpZip = Join-Path $env:TEMP "corepulse_lhm_096.zip"
    $tmpExt = Join-Path $env:TEMP "corepulse_lhm_096_ext"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip" -OutFile $tmpZip -UseBasicParsing
    if (Test-Path $tmpExt) { Remove-Item $tmpExt -Recurse -Force }
    Expand-Archive -Path $tmpZip -DestinationPath $tmpExt -Force
    foreach ($f in @("LibreHardwareMonitorLib.dll","HidSharp.dll","DiskInfoToolkit.dll","RAMSPDToolkit-NDD.dll","BlackSharp.Core.dll","System.Memory.dll","System.Buffers.dll","System.Numerics.Vectors.dll","System.Runtime.CompilerServices.Unsafe.dll")) {
        $src = Join-Path $tmpExt $f
        if (Test-Path $src) { Copy-Item $src $libDir -Force }
    }
    Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
    Remove-Item $tmpExt -Recurse -Force -ErrorAction SilentlyContinue
}

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

Write-Host "Compiling CorePulse.exe..."
& $csc `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:x64 `
    /codepage:65001 `
    /utf8output `
    /win32icon:"$scriptDir\app.ico" `
    /win32manifest:"$scriptDir\app.manifest" `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Management.dll `
    /reference:"$lhmDll" `
    /out:"$scriptDir\CorePulse.exe" `
    "$scriptDir\CoreNative.cs" `
    "$scriptDir\FpsMonitor.cs" `
    "$scriptDir\HardwareEngine.cs" `
    "$scriptDir\FanController.cs" `
    "$scriptDir\OverlayHudForm.cs" `
    "$scriptDir\SensorPickerForm.cs" `
    "$scriptDir\Program.cs"

if ($LASTEXITCODE -ne 0) { throw "Compilation failed (ExitCode=$LASTEXITCODE)" }

$exeInfo = Get-Item (Join-Path $scriptDir "CorePulse.exe")
Write-Host ("Successfully built: {0} ({1:N1} KB)" -f $exeInfo.FullName, ($exeInfo.Length / 1024.0))
