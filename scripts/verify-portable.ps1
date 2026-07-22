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
$verifyRoot=Join-Path ([IO.Path]::GetTempPath()) ('HomeKTV-Portable-Verify-'+[Guid]::NewGuid().ToString('N'));$resolvedVerify=[IO.Path]::GetFullPath($verifyRoot);if(-not $resolvedVerify.StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase)){throw '临时验证路径不安全。'}
try{
    New-Item -ItemType Directory -Path $resolvedVerify|Out-Null
    Copy-Item -LiteralPath $portable -Destination $resolvedVerify -Recurse
    $copy=Join-Path $resolvedVerify (Split-Path $portable -Leaf);$process=Start-Process -FilePath (Join-Path $copy 'HomeKTV.exe') -ArgumentList '--health-check' -WorkingDirectory $copy -WindowStyle Hidden -PassThru;if(-not $process.WaitForExit(30000)){Stop-Process -Id $process.Id -Force;throw '复制路径健康检查超时。'};if($process.ExitCode -ne 0){throw "复制路径健康检查失败：$($process.ExitCode)"}
    $ui=Start-Process -FilePath (Join-Path $copy 'HomeKTV.exe') -ArgumentList '--smoke-ui' -WorkingDirectory $copy -WindowStyle Hidden -PassThru;if(-not $ui.WaitForExit(90000)){Stop-Process -Id $ui.Id -Force;throw 'WPF/LibVLC/服务器启动与安全退出烟测超时。'};if($ui.ExitCode -ne 0){throw "WPF 启动烟测失败：$($ui.ExitCode)"}
}finally{if(Test-Path -LiteralPath $resolvedVerify){Remove-Item -LiteralPath $resolvedVerify -Recurse -Force}}
Write-Host '[HomeKTV] PASS：目录、LibVLC、FFmpeg、Web、相对配置和复制路径健康检查全部通过。' -ForegroundColor Green
