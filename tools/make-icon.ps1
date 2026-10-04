# Regenerates src/ScopeTrace/Assets/ScopeTrace.ico (Windows PowerShell 5.1):
#   powershell -File tools/make-icon.ps1 -OutIco src/ScopeTrace/Assets/ScopeTrace.ico -PreviewPng icon.png
param([string]$OutIco, [string]$PreviewPng)
Add-Type -AssemblyName System.Drawing
function Draw([int]$n) {
  $bmp = New-Object System.Drawing.Bitmap $n, $n, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
  $s = $n / 256.0
  function RR($x,$y,$w,$h,$r) { $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $d=2*$r; $p.AddArc($x,$y,$d,$d,180,90); $p.AddArc($x+$w-$d,$y,$d,$d,270,90); $p.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90); $p.AddArc($x,$y+$h-$d,$d,$d,90,90); $p.CloseFigure(); $p }
  # bezel
  $bez = RR (8*$s) (8*$s) (240*$s) (240*$s) (44*$s)
  $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0,0), (New-Object System.Drawing.PointF 0,$n), ([System.Drawing.Color]::FromArgb(255,0x4A,0x50,0x57)), ([System.Drawing.Color]::FromArgb(255,0x23,0x27,0x2B)))
  $g.FillPath($br, $bez)
  # screen
  $m = 30*$s; $scr = RR $m $m ($n-2*$m) ($n-2*$m) (24*$s)
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,0x04,0x12,0x08))), $scr)
  $g.SetClip($scr)
  if ($n -ge 48) {
    $gp = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150,0x1F,0x6B,0x38)), ([Math]::Max(1,2*$s))
    for ($i=1; $i -lt 4; $i++) { $x = $m + $i*($n-2*$m)/4; $g.DrawLine($gp, $x, $m, $x, $n-$m); $y = $m + $i*($n-2*$m)/4; $g.DrawLine($gp, $m, $y, $n-$m, $y) }
  }
  # trace: sine then square edge
  $pts = New-Object System.Collections.Generic.List[System.Drawing.PointF]
  $x0 = $m; $w = $n-2*$m; $cy = $n/2; $amp = 52*$s
  for ($k=0; $k -le 60; $k++) { $t = $k/60.0; $x = $x0 + $t*$w; $y = $cy - $amp*[Math]::Sin($t*2*[Math]::PI*1.5); $pts.Add((New-Object System.Drawing.PointF $x,$y)) }
  $arr = $pts.ToArray()
  $tw = [Math]::Max(1.6, 13*$s)
  if ($n -ge 32) { $glow = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70,0x39,0xFF,0x7A)), ($tw*3); $glow.LineJoin='Round'; $glow.StartCap='Round'; $glow.EndCap='Round'; $g.DrawLines($glow, $arr) }
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,0x39,0xFF,0x7A)), $tw; $pen.LineJoin='Round'; $pen.StartCap='Round'; $pen.EndCap='Round'
  $g.DrawLines($pen, $arr)
  $g.ResetClip()
  # screen rim highlight
  if ($n -ge 32) { $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90,0,0,0)), ([Math]::Max(1,3*$s))), $scr) }
  $g.Dispose(); $bmp
}
$sizes = 16,20,24,32,40,48,64,128,256
$pngs = @()
function Dib($b) {
  $n = $b.Width; $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
  $w.Write([uint32]40); $w.Write([int32]$n); $w.Write([int32]($n*2)); $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0); $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
  for ($y = $n - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $n; $x++) { $c = $b.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) } }
  $maskRow = [int]([Math]::Ceiling($n / 32.0) * 4); $w.Write((New-Object byte[] ($maskRow * $n)))
  $w.Flush(); return ,$ms.ToArray()
}
foreach ($n in $sizes) { $b = Draw $n; if ($n -ge 256) { $ms = New-Object IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngs += ,$ms.ToArray(); $b.Save($PreviewPng) } else { $pngs += ,(Dib $b) }; $b.Dispose() }
$fs = [IO.File]::Create($OutIco); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16*$sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) { $n=$sizes[$i]; $bw.Write([byte]($(if($n -ge 256){0}else{$n}))); $bw.Write([byte]($(if($n -ge 256){0}else{$n}))); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset); $offset += $pngs[$i].Length }
foreach ($p in $pngs) { $bw.Write([byte[]]$p) }
$bw.Close()
"ico written: $((Get-Item $OutIco).Length) bytes"
