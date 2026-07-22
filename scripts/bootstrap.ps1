[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Resolve-HomeKtvDotNet {
    if($env:HOMEKTV_DOTNET -and (Test-Path -LiteralPath $env:HOMEKTV_DOTNET)){return $env:HOMEKTV_DOTNET}
    $bundled=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-tools/dotnet10/dotnet.exe'
    if(Test-Path -LiteralPath $bundled){return $bundled}
    $command=Get-Command dotnet -ErrorAction SilentlyContinue
    if($command){return $command.Source}
    throw '未找到 .NET 10 SDK。请安装正式版 .NET 10 SDK，或设置 HOMEKTV_DOTNET。'
}

$dotnet=Resolve-HomeKtvDotNet
$sdkVersion=(& $dotnet --version).Trim()
if(-not $sdkVersion.StartsWith('10.')){throw "需要 .NET 10 SDK，当前为 $sdkVersion。"}
$node=Get-Command node -ErrorAction SilentlyContinue;if(-not $node){throw '未找到 Node.js。构建手机端需要 Node.js 20 或更高正式版。'}
$nodeVersion=(& node --version).TrimStart('v');if([int]($nodeVersion.Split('.')[0]) -lt 20){throw "Node.js 版本过低：$nodeVersion"}
if(-not(Get-Command npm -ErrorAction SilentlyContinue)){throw '未找到 npm。'}

Write-Host "[HomeKTV] .NET SDK $sdkVersion"
Write-Host "[HomeKTV] Node.js $nodeVersion / npm $(& npm --version)"
if(Get-Command git -ErrorAction SilentlyContinue){Write-Host "[HomeKTV] $(& git --version)"}else{Write-Warning 'Git 不在 PATH 中；不影响构建，但无法创建提交。'}

Push-Location $repoRoot
try{
    & $dotnet restore HomeKTV.sln --locked-mode --nologo
    if($LASTEXITCODE -ne 0){throw 'NuGet 还原失败。'}
    Push-Location 'src/HomeKTV.Web';try{& npm ci --no-audit --no-fund;if($LASTEXITCODE -ne 0){throw 'npm 依赖安装失败。'}}finally{Pop-Location}

    $ffmpegDir=Join-Path $repoRoot 'runtime-assets/FFmpeg';$ffmpeg=Join-Path $ffmpegDir 'ffmpeg.exe';$ffprobe=Join-Path $ffmpegDir 'ffprobe.exe'
    if(-not(Test-Path $ffmpeg) -or -not(Test-Path $ffprobe)){
        $cache=Join-Path $repoRoot '.bootstrap-cache';New-Item -ItemType Directory -Force -Path $cache,$ffmpegDir|Out-Null
        $archive=Join-Path $cache 'ffmpeg-8.0.1-essentials_build.zip'
        if(-not(Test-Path $archive)){Write-Host '[HomeKTV] 下载固定版本 FFmpeg 8.0.1…';Invoke-WebRequest -UseBasicParsing -Uri 'https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.0.1-essentials_build.zip' -OutFile $archive}
        $expected='e2aaeaa0fdbc397d4794828086424d4aaa2102cef1fb6874f6ffd29c0b88b673';$actual=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant();if($actual -ne $expected){throw "FFmpeg SHA-256 校验失败：$actual"}
        $extract=Join-Path $cache 'ffmpeg-8.0.1';New-Item -ItemType Directory -Force -Path $extract|Out-Null;Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force
        $source=Join-Path $extract 'ffmpeg-8.0.1-essentials_build';Copy-Item -LiteralPath (Join-Path $source 'bin/ffmpeg.exe'),(Join-Path $source 'bin/ffprobe.exe') -Destination $ffmpegDir -Force;Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $ffmpegDir 'LICENSE.txt') -Force
    }
    & $ffmpeg -version | Select-Object -First 1
    $vlc=Join-Path $env:USERPROFILE '.nuget/packages/videolan.libvlc.windows/3.0.23.1/build/x64/libvlc.dll';if(-not(Test-Path $vlc)){throw 'LibVLC x64 运行库没有正确还原。'}
    Write-Host '[HomeKTV] 引导完成。' -ForegroundColor Green
}finally{Pop-Location}
