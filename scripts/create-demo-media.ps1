[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop';$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));if(-not $OutputDirectory){$OutputDirectory=Join-Path $repoRoot 'demo-media'}
$ffmpeg=Join-Path $repoRoot 'runtime-assets/FFmpeg/ffmpeg.exe';if(-not(Test-Path $ffmpeg)){throw '请先运行 scripts/bootstrap.ps1 下载 FFmpeg。'}
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null;$video=Join-Path $OutputDirectory 'HomeKTV - 测试歌曲.mp4'
& $ffmpeg -v error -y -f lavfi -i 'testsrc2=size=1280x720:rate=30' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t 5 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest $video
if($LASTEXITCODE -ne 0){throw '测试视频生成失败。'}
$lrc=@'
[ar:HomeKTV]
[ti:测试歌曲]
[00:00.00]HomeKTV 便携版播放测试
[00:02.00]这一句用于验证 LRC 同步
[00:05.00]播放结束后应自动切到下一首
'@
Set-Content -LiteralPath (Join-Path $OutputDirectory 'HomeKTV - 测试歌曲.lrc') -Value $lrc -Encoding UTF8
& $ffmpeg -v error -y -f lavfi -i 'sine=frequency=330:sample_rate=44100' -t 5 -c:a libmp3lame -q:a 3 -metadata title='测试音频' -metadata artist='HomeKTV' (Join-Path $OutputDirectory 'HomeKTV - 测试音频.mp3')
if($LASTEXITCODE -ne 0){throw '测试 MP3 生成失败。'}
& $ffmpeg -v error -y -f lavfi -i 'sine=frequency=550:sample_rate=44100' -t 5 -c:a flac (Join-Path $OutputDirectory 'HomeKTV - 默认背景.flac')
if($LASTEXITCODE -ne 0){throw '测试 FLAC 生成失败。'}
foreach($index in 1..3){$color=@('0x7B2CBF','0x0077B6','0xD00000')[$index-1];& $ffmpeg -v error -y -f lavfi -i "color=c=$color`:s=640x360" -frames:v 1 (Join-Path $OutputDirectory ("slide-{0:00}.bmp" -f $index));if($LASTEXITCODE -ne 0){throw "测试图片 $index 生成失败。"}}
Write-Host "[HomeKTV] 已生成无版权测试媒体：$video" -ForegroundColor Green
