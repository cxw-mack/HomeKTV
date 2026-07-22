[CmdletBinding()]
param([int]$Port=16888)
$ErrorActionPreference='Stop';$root=Split-Path -Parent $MyInvocation.MyCommand.Path;$exe=Join-Path $root 'HomeKTV.exe';if(-not(Test-Path -LiteralPath $exe)){throw '请从 HomeKTV 便携版根目录运行此脚本。'}
Write-Host "将仅为当前 HomeKTV.exe 创建 Windows 专用网络 TCP 入站规则，端口 $Port。" -ForegroundColor Yellow
if((Read-Host '输入 YES 确认') -ne 'YES'){Write-Host '已取消。';exit 0}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent();$principal=New-Object Security.Principal.WindowsPrincipal($identity);if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){Start-Process powershell.exe -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$($MyInvocation.MyCommand.Path)`"",'-Port',$Port);exit}
$name='HomeKTV 家庭点歌（专用网络）';Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue|Remove-NetFirewallRule;New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Program $exe -Protocol TCP -LocalPort $Port -Profile Private|Out-Null;Write-Host '防火墙规则已添加。' -ForegroundColor Green

