[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop';$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));if(-not $OutputDirectory){$OutputDirectory=Join-Path $repoRoot 'demo-media'}
$ffmpeg=Join-Path $repoRoot 'runtime-assets/FFmpeg/ffmpeg.exe';if(-not(Test-Path $ffmpeg)){throw '请先运行 scripts/bootstrap.ps1 下载 FFmpeg。'}
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null;$video=Join-Path $OutputDirectory 'HomeKTV - 测试歌曲.mp4'
& $ffmpeg -y -f lavfi -i 'testsrc2=size=1280x720:rate=30' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t 8 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest $video
if($LASTEXITCODE -ne 0){throw '测试视频生成失败。'}
$lrc=@'
[ar:HomeKTV]
[ti:测试歌曲]
[00:00.00]HomeKTV 便携版播放测试
[00:02.00]这一句用于验证 LRC 同步
[00:05.00]播放结束后应自动切到下一首
'@
Set-Content -LiteralPath (Join-Path $OutputDirectory 'HomeKTV - 测试歌曲.lrc') -Value $lrc -Encoding UTF8
Write-Host "[HomeKTV] 已生成无版权测试媒体：$video" -ForegroundColor Green

