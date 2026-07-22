[CmdletBinding()]
param([string]$PortableDirectory)
$ErrorActionPreference='Stop';$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));if(-not $PortableDirectory){$PortableDirectory=Join-Path $repoRoot 'dist/HomeKTV-Portable-win-x64'};$portable=[IO.Path]::GetFullPath($PortableDirectory)
$requiredFiles=@('HomeKTV.exe','Start-HomeKTV.bat','Data/HomeKTV.db','Data/Settings.json','Runtime/LibVLC/libvlc.dll','Runtime/LibVLC/libvlccore.dll','Runtime/FFmpeg/ffmpeg.exe','Runtime/FFmpeg/ffprobe.exe','Web/index.html','THIRD_PARTY_NOTICES.md','README-使用说明.txt')
$requiredDirectories=@('Data/Backups','Media/MV','Media/Lyrics','Media/Covers','Media/Backgrounds','Media/ImportBox','Runtime/LibVLC/plugins','Runtime/FFmpeg','Web/assets','Logs','Licenses')
$missing=@();foreach($file in $requiredFiles){if(-not(Test-Path -LiteralPath (Join-Path $portable $file))){$missing+=$file}};foreach($directory in $requiredDirectories){if(-not(Test-Path -LiteralPath (Join-Path $portable $directory) -PathType Container)){$missing+=$directory}}
if($missing.Count){throw "便携包缺少：$($missing -join ', ')"}
if((Get-ChildItem -LiteralPath (Join-Path $portable 'Runtime/LibVLC/plugins') -Recurse -Filter '*.dll').Count -lt 20){throw 'LibVLC 插件目录不完整。'}
& (Join-Path $portable 'Runtime/FFmpeg/ffprobe.exe') -version|Select-Object -First 1;if($LASTEXITCODE -ne 0){throw 'FFprobe 不能运行。'}
$settings=Get-Content -LiteralPath (Join-Path $portable 'Data/Settings.json') -Raw;if($settings -match '(?i)[A-Z]:\\'){throw 'Settings.json 包含硬编码绝对盘符。'}
$unexpected=Get-ChildItem -LiteralPath $portable -Directory -Recurse|Where-Object {$_.Name -in @('node_modules','obj','bin','tests','TestResults')};if($unexpected){throw "便携包包含开发目录：$($unexpected.FullName -join ', ')"}
$textFiles=Get-ChildItem -LiteralPath $portable -File -Recurse|Where-Object {$_.Extension -in @('.json','.txt','.bat','.ps1','.html','.js','.css','.md','.log')};if($textFiles -and (Select-String -LiteralPath $textFiles.FullName -SimpleMatch $repoRoot -Quiet)){throw '便携包文本资源泄漏了开发目录路径。'}
if($textFiles -and (Select-String -LiteralPath $textFiles.FullName -Pattern '(?i)[A-Z]:\\Users\\|/Users/|\\CXW\\' -Quiet)){throw '便携包文本资源包含开发机用户或用户目录。'}
$webEntry=Get-Content -LiteralPath (Join-Path $portable 'Web/index.html') -Raw;if($webEntry -match '(?i)<(?:script|link)[^>]+(?:src|href)=["''][ ]*https?://'){throw '手机页入口依赖远程 CDN，断网时无法启动。'}
$verifyRoot=Join-Path ([IO.Path]::GetTempPath()) ('HomeKTV-Portable-Verify-'+[Guid]::NewGuid().ToString('N'));$resolvedVerify=[IO.Path]::GetFullPath($verifyRoot);if(-not $resolvedVerify.StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase)){throw '临时验证路径不安全。'}
function Invoke-Smoke([string]$Executable,[string[]]$Arguments,[int]$TimeoutMs,[string]$Description){$process=Start-Process -FilePath $Executable -ArgumentList $Arguments -WorkingDirectory (Split-Path $Executable -Parent) -WindowStyle Hidden -PassThru;if(-not $process.WaitForExit($TimeoutMs)){Stop-Process -Id $process.Id -Force;throw "$Description 超时。"};if($process.ExitCode -ne 0){throw "$Description 失败：$($process.ExitCode)"};$process.Dispose()}
function Remove-VerifyDirectory([string]$Path){$resolved=[IO.Path]::GetFullPath($Path);$allowedPrefix=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'HomeKTV-Portable-Verify-'));if(-not $resolved.StartsWith($allowedPrefix,[StringComparison]::OrdinalIgnoreCase)){throw "拒绝清理非验证目录：$resolved"};for($attempt=0;$attempt -lt 20;$attempt++){try{if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force};return}catch [IO.IOException]{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 250}}}
Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter 'HomeKTV-Portable-Verify-*' -ErrorAction SilentlyContinue|ForEach-Object{Remove-VerifyDirectory $_.FullName}
try{
    New-Item -ItemType Directory -Path $resolvedVerify|Out-Null
    Copy-Item -LiteralPath $portable -Destination $resolvedVerify -Recurse
    $copy=Join-Path $resolvedVerify (Split-Path $portable -Leaf);$exe=Join-Path $copy 'HomeKTV.exe';Invoke-Smoke $exe @('--health-check') 30000 '复制路径健康检查'

    $configured=Get-Content -LiteralPath (Join-Path $copy 'Data/Settings.json') -Raw|ConvertFrom-Json;$listener=$null;try{$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Any,[int]$configured.ServerPort);$listener.Start()}catch [Net.Sockets.SocketException]{}try{Invoke-Smoke $exe @('--server-smoke') 90000 '端口冲突回退与手机服务健康检查'}finally{if($listener){$listener.Stop()}}

    $settingsPath=Join-Path $copy 'Data/Settings.json';$settingsBackup=$settingsPath+'.verify-backup';Move-Item -LiteralPath $settingsPath -Destination $settingsBackup;Set-Content -LiteralPath $settingsPath -Value '{broken' -Encoding UTF8;Invoke-Smoke $exe @('--health-check') 30000 '损坏配置恢复检查';Remove-Item -LiteralPath $settingsPath -Force;Get-ChildItem -LiteralPath (Join-Path $copy 'Data') -Filter 'Settings.corrupt-*.json'|Remove-Item -Force;Move-Item -LiteralPath $settingsBackup -Destination $settingsPath

    $databasePath=Join-Path $copy 'Data/HomeKTV.db';$databaseBackup=$databasePath+'.verify-backup';Move-Item -LiteralPath $databasePath -Destination $databaseBackup;Invoke-Smoke $exe @('--health-check') 30000 '首次无数据库初始化检查';foreach($suffix in @('','-wal','-shm')){$candidate=$databasePath+$suffix;if(Test-Path -LiteralPath $candidate){Remove-Item -LiteralPath $candidate -Force}};Move-Item -LiteralPath $databaseBackup -Destination $databasePath

    $ffmpegPath=Join-Path $copy 'Runtime/FFmpeg';$ffmpegBackup=Join-Path $copy 'Runtime/FFmpeg.verify-backup';Move-Item -LiteralPath $ffmpegPath -Destination $ffmpegBackup;try{Invoke-Smoke $exe @('--smoke-ui') 90000 '缺少 FFmpeg 时的 WPF/LibVLC 降级启动检查'}finally{if(Test-Path -LiteralPath $ffmpegPath){Remove-Item -LiteralPath $ffmpegPath -Recurse -Force};Move-Item -LiteralPath $ffmpegBackup -Destination $ffmpegPath}
}finally{Remove-VerifyDirectory $resolvedVerify}
Write-Host '[HomeKTV] PASS：目录/依赖/Web/相对路径/端口冲突/配置恢复/首次建库/FFmpeg 降级启动全部通过。' -ForegroundColor Green
