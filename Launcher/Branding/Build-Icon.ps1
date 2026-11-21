$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$xaml=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'HubIcon.xaml'))
$images=@()
foreach($size in @(16,24,32,48,64,128,256)){
 $art=[Windows.Markup.XamlReader]::Parse($xaml)
 $view=New-Object Windows.Controls.Viewbox
 $view.Child=$art;$view.Width=$size;$view.Height=$size
 $view.Measure([Windows.Size]::new($size,$size));$view.Arrange([Windows.Rect]::new(0,0,$size,$size));$view.UpdateLayout()
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($view)
 $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder;$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $memory=New-Object IO.MemoryStream;$encoder.Save($memory);$bytes=$memory.ToArray();$memory.Dispose()
 $images+=,@{Size=$size;Bytes=$bytes}
 if($size -eq 256){[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'Launcher.png'),$bytes)}
}
$writer=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path (Split-Path $PSScriptRoot) 'Launcher.ico')))
try{
 $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$images.Count);$offset=6+16*$images.Count
 foreach($entry in $images){$sizeByte=if($entry.Size -eq 256){0}else{$entry.Size};$writer.Write([byte]$sizeByte);$writer.Write([byte]$sizeByte);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$entry.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$entry.Bytes.Length}
 foreach($entry in $images){$writer.Write([byte[]]$entry.Bytes)}
}finally{$writer.Dispose()}
