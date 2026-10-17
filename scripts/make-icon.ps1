param([string]$Destination = (Join-Path $PSScriptRoot '..\assets\app.ico'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$entries=New-Object 'System.Collections.Generic.List[object]'
foreach($size in @(16,32,48,256)){
  $bitmap=New-Object Drawing.Bitmap($size,$size)
  $graphics=[Drawing.Graphics]::FromImage($bitmap)
  $memory=New-Object IO.MemoryStream
  $brushes=@()
  $pen=$null
  try{
    $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($size/64.0),($size/64.0))
    $graphics.Clear([Drawing.Color]::Transparent)
    $navy=New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#142d4e'))
    $blue=New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#479fe8'))
    $light=New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#a6d8ff'))
    $amber=New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#f7b543'))
    $brushes=@($navy,$blue,$light,$amber)
    $shape=New-Object Drawing.Drawing2D.GraphicsPath
    try{
      $shape.AddArc(2,2,24,24,180,90);$shape.AddArc(38,2,24,24,270,90)
      $shape.AddArc(38,38,24,24,0,90);$shape.AddArc(2,38,24,24,90,90);$shape.CloseFigure()
      $graphics.FillPath($navy,$shape)
    }finally{$shape.Dispose()}
    $graphics.FillRectangle($blue,14,16,36,29)
    $graphics.FillRectangle($light,14,16,36,8)
    $graphics.FillEllipse($amber,33,32,26,26)
    $pen=New-Object Drawing.Pen($navy.Color,5)
    $pen.StartCap=[Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap=[Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin=[Drawing.Drawing2D.LineJoin]::Round
    $points=[Drawing.PointF[]]@((New-Object Drawing.PointF(39,45)),(New-Object Drawing.PointF(44,50)),(New-Object Drawing.PointF(53,39)))
    $graphics.DrawLines($pen,$points)
    $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $entries.Add([pscustomobject]@{Size=$size;Bytes=$memory.ToArray()})
  }finally{if($pen){$pen.Dispose()};foreach($brush in $brushes){$brush.Dispose()};$memory.Dispose();$graphics.Dispose();$bitmap.Dispose()}
}
$destinationPath=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destinationPath)) -Force|Out-Null
$writer=New-Object IO.BinaryWriter([IO.File]::Open($destinationPath,[IO.FileMode]::Create))
try{
  $writer.Write([UInt16]0);$writer.Write([UInt16]1);$writer.Write([UInt16]$entries.Count)
  $offset=6+16*$entries.Count
  foreach($entry in $entries){
    $dimension=if($entry.Size -eq 256){0}else{$entry.Size}
    $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
    $writer.Write([UInt16]1);$writer.Write([UInt16]32);$writer.Write([UInt32]$entry.Bytes.Length);$writer.Write([UInt32]$offset)
    $offset+=$entry.Bytes.Length
  }
  foreach($entry in $entries){$writer.Write([byte[]]$entry.Bytes)}
}finally{$writer.Dispose()}
