param([switch]$Preview)
$ErrorActionPreference='Stop'
$framework='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$sourceDir=(Join-Path $PSScriptRoot 'src').Replace('/','\')
$out=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist/v0.4.6'))
if($Preview){$out=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist/preview'))}
New-Item -ItemType Directory -Force $out | Out-Null
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object { '/r:'+(Join-Path $framework $_) }
$refs+=@('System.Xaml.dll') | ForEach-Object {'/r:'+(Join-Path $framework $_)}
$refs+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object {'/r:'+(Join-Path "$framework\WPF" $_)}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$toolZip=Join-Path $out 'tools.zip'
if(Test-Path -LiteralPath $toolZip){Remove-Item -LiteralPath $toolZip -Force}
$zip=[IO.Compression.ZipFile]::Open($toolZip,[IO.Compression.ZipArchiveMode]::Create)
try {foreach($name in @('采集卡改名.exe','OBS改名.exe','OBS蒙版.exe','虚拟显示器.exe')){$file=Join-Path $sourceDir ('assets/tools/'+$name);if(Test-Path -LiteralPath $file){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file,$name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}}finally{$zip.Dispose()}
$resources=@('app.ico','app.png','save.png','restore.png','customer-qr.jpg','tool0.png','tool1.png','tool2.png','tool3.png','guide-home.png','guide-save.png','guide-library.png','guide-restored.png')|ForEach-Object {$asset=Join-Path $sourceDir ('assets/'+$_);if(!(Test-Path -LiteralPath $asset)){if($_ -notmatch '^tool[0-3]\.png$'){throw "缺少资源：$_"};$asset=Join-Path $sourceDir 'assets/app.png'};"/resource:$asset,$_"}
$resources+="/resource:$sourceDir\ui.xaml,ui.xaml"
$resources+="/resource:$toolZip,tools.zip"
$sources=@('CompanionBackup.cs','ManagerWindow.cs','AppStorage.cs','BundleStore.cs','Devices.cs','MigrationPlan.cs','CompanionLauncher.cs','AdvancedTests.cs','ToolBox.cs','AssemblyInfo.cs')|ForEach-Object {Join-Path $sourceDir $_}
$define=if($Preview){'/define:UI_DEMO'}else{'/define:RELEASE'}
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 $define "/win32icon:$sourceDir\assets\app.ico" "/out:$out\直播伴侣备份管家.exe" $resources $refs $sources
if($LASTEXITCODE -ne 0){throw '编译失败'}
if($Preview){return}
$process=Start-Process -FilePath "$out/直播伴侣备份管家.exe" -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0){throw '自检失败，查看 last-error.txt'}
Get-Content -LiteralPath "$out/self-test-result.txt"
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/win32icon:$sourceDir\assets\app.ico" "/resource:$sourceDir\assets\app.ico,app.ico" "/resource:$sourceDir\assets\app.png,app.png" "/resource:$out\直播伴侣备份管家.exe,application.exe" "/out:$out\直播伴侣备份管家-安装-v0.4.6.exe" $refs "$sourceDir\Setup.cs" "$sourceDir\AssemblyInfo.cs"
if($LASTEXITCODE -ne 0){throw '安装包编译失败'}
Get-Item -LiteralPath "$out/直播伴侣备份管家.exe" | Select-Object FullName,Length
