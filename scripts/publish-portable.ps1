[CmdletBinding()]
param()
$ErrorActionPreference='Stop';$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$dist=[IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'));$expected=[IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'));$target=Join-Path $dist 'HomeKTV-Portable-win-x64';$preserveRoot=Join-Path $repoRoot ('.homektv-publish-preserve-'+[Guid]::NewGuid().ToString('N'));$stateRestored=$false;$publishCompleted=$false
if($dist -ne $expected -or -not $dist.StartsWith($repoRoot,[StringComparison]::OrdinalIgnoreCase)){throw '拒绝清理意外的发布路径。'}
$bundledDotNet=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-tools/dotnet10/dotnet.exe';$dotnet=if($env:HOMEKTV_DOTNET){$env:HOMEKTV_DOTNET}elseif(Test-Path $bundledDotNet){$bundledDotNet}elseif(Get-Command dotnet -ErrorAction SilentlyContinue){(Get-Command dotnet).Source}else{''}
function Restore-HomeKtvState {
    if($script:stateRestored -or -not (Test-Path -LiteralPath $script:preserveRoot)){return}
    if($script:publishCompleted){New-Item -ItemType Directory -Force -Path $script:target|Out-Null;foreach($name in @('Data','Media','Logs','Models')){$saved=Join-Path $script:preserveRoot $name;if(-not(Test-Path -LiteralPath $saved)){continue};$destination=Join-Path $script:target $name;if(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination -Recurse -Force};Move-Item -LiteralPath $saved -Destination $destination};Remove-Item -LiteralPath $script:preserveRoot -Recurse -Force -ErrorAction SilentlyContinue}
    else{if(Test-Path -LiteralPath $script:target){Remove-Item -LiteralPath $script:target -Recurse -Force};New-Item -ItemType Directory -Force -Path (Split-Path -Parent $script:target)|Out-Null;Move-Item -LiteralPath $script:preserveRoot -Destination $script:target}
    $script:stateRestored=$true
}
if(Get-Process -Name 'HomeKTV' -ErrorAction SilentlyContinue){throw '发布前请关闭 HomeKTV，以便安全保留数据库与媒体。'}
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release
Push-Location $repoRoot
try{
    if(Test-Path -LiteralPath $target){Move-Item -LiteralPath $target -Destination $preserveRoot}
    if(Test-Path -LiteralPath $dist){Remove-Item -LiteralPath $dist -Recurse -Force};New-Item -ItemType Directory -Path $dist|Out-Null
    $stage=Join-Path $dist '.publish-win-x64';New-Item -ItemType Directory -Path $stage,$target|Out-Null
    & $dotnet clean 'src/HomeKTV.App/HomeKTV.App.csproj' -c Release -r win-x64 --nologo;if($LASTEXITCODE -ne 0){throw 'HomeKTV 发布前清理失败。'}
    & $dotnet publish 'src/HomeKTV.App/HomeKTV.App.csproj' -c Release -r win-x64 --self-contained true --no-restore -o $stage -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:PathMap="$repoRoot=/_/src" -p:IncludeNativeLibrariesForSelfExtract=false
    if($LASTEXITCODE -ne 0){throw 'HomeKTV 发布失败。'}
    Get-ChildItem -LiteralPath $stage -Force|Where-Object Name -ne 'libvlc'|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force}
    $vlcSource=Join-Path $stage 'libvlc/win-x64';if(-not(Test-Path (Join-Path $vlcSource 'libvlc.dll'))){throw '发布输出中缺少 LibVLC x64。'}
    $vlcTarget=Join-Path $target 'Runtime/LibVLC';$ffmpegTarget=Join-Path $target 'Runtime/FFmpeg';New-Item -ItemType Directory -Force -Path $vlcTarget,$ffmpegTarget|Out-Null;Get-ChildItem -LiteralPath $vlcSource -Force|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $vlcTarget -Recurse -Force}
    $ffmpegSource=Join-Path $repoRoot 'runtime-assets/FFmpeg';if(-not(Test-Path (Join-Path $ffmpegSource 'ffprobe.exe'))){throw '缺少 FFmpeg 资产，请先运行 bootstrap.ps1。'};Get-ChildItem -LiteralPath $ffmpegSource -Force|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $ffmpegTarget -Force}
    foreach($directory in @('Data/Backups','Media/MV','Media/Audio','Media/Lyrics','Media/Covers','Media/Backgrounds','Media/ImportBox','Media/Slideshows/Songs','Media/Slideshows/Defaults','Media/Generated','Web','Logs','Models','Licenses')){New-Item -ItemType Directory -Force -Path (Join-Path $target $directory)|Out-Null}
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/HomeKTV.Web/dist') -Force|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target 'Web') -Recurse -Force}
    Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md'),(Join-Path $repoRoot 'README-使用说明.txt'),(Join-Path $repoRoot 'Start-HomeKTV.bat'),(Join-Path $repoRoot 'Configure-Firewall.ps1') -Destination $target -Force
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Licenses') -Force|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target 'Licenses') -Force}
    $demoStage=Join-Path $dist '.demo-media';& (Join-Path $PSScriptRoot 'create-demo-media.ps1') -OutputDirectory $demoStage;Get-ChildItem -LiteralPath $demoStage -File|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target 'Media/ImportBox') -Force}
    $health=Start-Process -FilePath (Join-Path $target 'HomeKTV.exe') -ArgumentList @('--portable-media-smoke','--health-check') -WorkingDirectory $target -WindowStyle Hidden -PassThru;if(-not $health.WaitForExit(120000)){Stop-Process -Id $health.Id -Force;throw '便携版 MV/音频/幻灯片混合播放健康检查超时。'};if($health.ExitCode -ne 0){throw "便携版健康检查失败：$($health.ExitCode)"};$health.Dispose()
    Remove-Item -LiteralPath $demoStage -Recurse -Force;Get-ChildItem -LiteralPath (Join-Path $target 'Media/ImportBox') -File|Where-Object{$_.Name -like 'HomeKTV - 测试*' -or $_.Name -eq 'HomeKTV - 默认背景.flac' -or $_.Name -like 'slide-*.bmp'}|Remove-Item -Force
    foreach($mediaDirectory in @('Media/MV','Media/Audio','Media/Lyrics','Media/Slideshows/Songs')){Get-ChildItem -LiteralPath (Join-Path $target $mediaDirectory) -File -Recurse -ErrorAction SilentlyContinue|Remove-Item -Force}
    foreach($suffix in @('','-journal','-wal','-shm')){$databaseFile=(Join-Path $target 'Data/HomeKTV.db')+$suffix;if(Test-Path -LiteralPath $databaseFile){Remove-Item -LiteralPath $databaseFile -Force}}
    $cleanHealth=Start-Process -FilePath (Join-Path $target 'HomeKTV.exe') -ArgumentList @('--health-check') -WorkingDirectory $target -WindowStyle Hidden -PassThru;if(-not $cleanHealth.WaitForExit(30000)){Stop-Process -Id $cleanHealth.Id -Force;throw '清洁便携数据库初始化超时。'};if($cleanHealth.ExitCode -ne 0){throw "清洁便携数据库初始化失败：$($cleanHealth.ExitCode)"};$cleanHealth.Dispose()
    foreach($suffix in @('-journal','-wal','-shm')){$sidecar=(Join-Path $target 'Data/HomeKTV.db')+$suffix;if(Test-Path -LiteralPath $sidecar){Remove-Item -LiteralPath $sidecar -Force}}
    Get-ChildItem -LiteralPath (Join-Path $target 'Logs') -File -Recurse -ErrorAction SilentlyContinue|Remove-Item -Force
    Remove-Item -LiteralPath $stage -Recurse -Force
    $exe=Join-Path $target 'HomeKTV.exe';for($attempt=0;$attempt -lt 20;$attempt++){try{$stream=[IO.File]::Open($exe,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite);$stream.Dispose();break}catch [IO.IOException]{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 500}}
    $portableBytes=(Get-ChildItem -LiteralPath $target -File -Recurse|Measure-Object Length -Sum).Sum
    $zip=Join-Path $dist 'HomeKTV-Portable-win-x64.zip';if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip -Force};Add-Type -AssemblyName System.IO.Compression.FileSystem;[IO.Compression.ZipFile]::CreateFromDirectory($target,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
    $zipBytes=(Get-Item -LiteralPath $zip).Length
    $publishCompleted=$true
    Restore-HomeKtvState
    Write-Host "[HomeKTV] 便携版：$target（$([math]::Round($portableBytes/1MB,1)) MB，不含已保留的用户媒体）" -ForegroundColor Green;Write-Host "[HomeKTV] ZIP：$zip（$([math]::Round($zipBytes/1MB,1)) MB）" -ForegroundColor Green
}finally{Pop-Location;Restore-HomeKtvState}
