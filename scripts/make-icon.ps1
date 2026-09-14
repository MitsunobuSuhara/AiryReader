$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing
$taskSource = [Drawing.Bitmap]::new((Join-Path $taskRoot 'assets\icons\airyPDF-crane.png'))
$taskSizes = @(16,24,32,48,64,128,256)
$taskPngs = [Collections.Generic.List[byte[]]]::new()
try {
    foreach ($taskSize in $taskSizes) {
        $taskBitmap = [Drawing.Bitmap]::new($taskSize,$taskSize)
        $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
        $taskMemory = [IO.MemoryStream]::new()
        try {
            $taskGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            if ($taskSize -le 64) {
                # Small Windows icons need broad facets and a thicker neck.
                $taskGraphics.Clear([Drawing.Color]::Black)
                $taskGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
                $taskGraphics.ScaleTransform($taskSize/16.0,$taskSize/16.0)
                $taskFacets = @(
                    @(1,6, 3.5,4, 5,5, 7,9, 5.5,12, 3.5,6),
                    @(7,8.5, 11.5,1.5, 10,10.5),
                    @(6,12.5, 7.5,10, 10,11.5, 9,14),
                    @(10,13.5, 11,11, 15,6)
                )
                foreach ($taskFacet in $taskFacets) {
                    $taskPoints = [Collections.Generic.List[Drawing.PointF]]::new()
                    for ($taskPoint=0; $taskPoint -lt $taskFacet.Count; $taskPoint+=2) {
                        $taskPoints.Add([Drawing.PointF]::new($taskFacet[$taskPoint],$taskFacet[$taskPoint+1]))
                    }
                    $taskGraphics.FillPolygon([Drawing.Brushes]::White,$taskPoints.ToArray())
                }
            } else {
                $taskGraphics.DrawImage($taskSource,0,0,$taskSize,$taskSize)
            }
            $taskBitmap.Save($taskMemory,[Drawing.Imaging.ImageFormat]::Png)
            $taskPngs.Add($taskMemory.ToArray())
        } finally { $taskMemory.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose() }
    }
    $taskFile = [IO.File]::Create((Join-Path $taskRoot 'assets\icons\airyPDF.ico'))
    $taskWriter = [IO.BinaryWriter]::new($taskFile)
    try {
        $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskSizes.Count)
        $taskOffset = 6 + 16*$taskSizes.Count
        for ($taskIndex=0; $taskIndex -lt $taskSizes.Count; $taskIndex++) {
            $taskByteSize = if ($taskSizes[$taskIndex] -eq 256) {0} else {$taskSizes[$taskIndex]}
            $taskWriter.Write([byte]$taskByteSize); $taskWriter.Write([byte]$taskByteSize)
            $taskWriter.Write([byte]0); $taskWriter.Write([byte]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
            $taskWriter.Write([uint32]$taskPngs[$taskIndex].Length); $taskWriter.Write([uint32]$taskOffset)
            $taskOffset += $taskPngs[$taskIndex].Length
        }
        foreach ($taskPng in $taskPngs) { $taskWriter.Write($taskPng) }
    } finally { $taskWriter.Dispose(); $taskFile.Dispose() }
} finally { $taskSource.Dispose() }
