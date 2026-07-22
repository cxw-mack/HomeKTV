[CmdletBinding()]
param()
$ErrorActionPreference='Stop';$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$dist=[IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'));$expected=[IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
if($dist -ne $expected -or -not $dist.StartsWith($repoRoot,[StringComparison]::OrdinalIgnoreCase)){throw '拒绝清理意外的发布路径。'}
$bundledDotNet=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-tools/dotnet10/dotnet.exe';$dotnet=if($env:HOMEKTV_DOTNET){$env:HOMEKTV_DOTNET}elseif(Test-Path $bundledDotNet){$bundledDotNet}elseif(Get-Command dotnet -ErrorAction SilentlyContinue){(Get-Command dotnet).Source}else{''}
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release
if(Test-Path -LiteralPath $dist){Remove-Item -LiteralPath $dist -Recurse -Force};New-Item -ItemType Directory -Path $dist|Out-Null
$stage=Join-Path $dist '.publish-win-x64';$target=Join-Path $dist 'HomeKTV-Portable-win-x64';New-Item -ItemType Directory -Path $stage,$target|Out-Null
Push-Location $repoRoot
try{
    & $dotnet publish 'src/HomeKTV.App/HomeKTV.App.csproj' -c Release -r win-x64 --self-contained true --no-restore -o $stage -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=embedded -p:IncludeNativeLibrariesForSelfExtract=false
    if($LASTEXITCODE -ne 0){throw 'HomeKTV 发布失败。'}
    Get-ChildItem -LiteralPath $stage -Force|Where-Object Name -ne 'libvlc'|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force}
    $vlcSource=Join-Path $stage 'libvlc/win-x64';if(-not(Test-Path (Join-Path $vlcSource 'libvlc.dll'))){throw '发布输出中缺少 LibVLC x64。'}
    $vlcTarget=Join-Path $target 'Runtime/LibVLC';$ffmpegTarget=Join-Path $target 'Runtime/FFmpeg';New-Item -ItemType Directory -Force -Path $vlcTarget,$ffmpegTarget|Out-Null;Get-ChildItem -LiteralPath $vlcSource -Force|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $vlcTarget -Recurse -Force}
    $ffmpegSource=Join-Path $repoRoot 'runtime-assets/FFmpeg';if(-not(Test-Path (Join-Path $ffmpegSource 'ffprobe.exe'))){throw '缺少 FFmpeg 资产，请先运行 bootstrap.ps1。'};Get-ChildItem -LiteralPath $ffmpegSource -Force|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $ffmpegTarget -Force}
    foreach($directory in @('Data/Backups','Media/MV','Media/Lyrics','Media/Covers','Media/Backgrounds','Media/ImportBox','Web','Logs','Licenses')){New-Item -ItemType Directory -Force -Path (Join-Path $target $directory)|Out-Null}
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/HomeKTV.Web/dist') -Force|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target 'Web') -Recurse -Force}
    Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md'),(Join-Path $repoRoot 'README-使用说明.txt'),(Join-Path $repoRoot 'Start-HomeKTV.bat'),(Join-Path $repoRoot 'Configure-Firewall.ps1') -Destination $target -Force
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Licenses') -Force|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target 'Licenses') -Force}
    $demoStage=Join-Path $dist '.demo-media';& (Join-Path $PSScriptRoot 'create-demo-media.ps1') -OutputDirectory $demoStage;Copy-Item -LiteralPath (Join-Path $demoStage 'HomeKTV - 测试歌曲.mp4'),(Join-Path $demoStage 'HomeKTV - 测试歌曲.lrc') -Destination (Join-Path $target 'Media/ImportBox') -Force
    $health=Start-Process -FilePath (Join-Path $target 'HomeKTV.exe') -ArgumentList @('--import-demo','--playback-smoke','--health-check') -WorkingDirectory $target -WindowStyle Hidden -PassThru;if(-not $health.WaitForExit(90000)){Stop-Process -Id $health.Id -Force;throw '便携版导入、播放与初始化健康检查超时。'};if($health.ExitCode -ne 0){throw "便携版健康检查失败：$($health.ExitCode)"};$health.Dispose()
    Remove-Item -LiteralPath $demoStage -Recurse -Force;Remove-Item -LiteralPath (Join-Path $target 'Media/ImportBox/HomeKTV - 测试歌曲.mp4'),(Join-Path $target 'Media/ImportBox/HomeKTV - 测试歌曲.lrc') -Force
    Remove-Item -LiteralPath $stage -Recurse -Force
    $exe=Join-Path $target 'HomeKTV.exe';for($attempt=0;$attempt -lt 20;$attempt++){try{$stream=[IO.File]::Open($exe,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite);$stream.Dispose();break}catch [IO.IOException]{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 500}}
    $zip=Join-Path $dist 'HomeKTV-Portable-win-x64.zip';Compress-Archive -LiteralPath $target -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "[HomeKTV] 便携版：$target" -ForegroundColor Green;Write-Host "[HomeKTV] ZIP：$zip" -ForegroundColor Green
}finally{Pop-Location}
