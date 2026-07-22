[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop';$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bundledDotNet=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-tools/dotnet10/dotnet.exe';$dotnet=if($env:HOMEKTV_DOTNET){$env:HOMEKTV_DOTNET}elseif(Test-Path $bundledDotNet){$bundledDotNet}elseif(Get-Command dotnet -ErrorAction SilentlyContinue){(Get-Command dotnet).Source}else{''}
if(-not(Test-Path $dotnet)){throw '未找到 .NET 10 SDK；请先运行 bootstrap.ps1。'}
Push-Location $repoRoot
try{
    Push-Location 'src/HomeKTV.Web';try{& npm run build;if($LASTEXITCODE -ne 0){throw 'Vue 手机端构建失败。'}}finally{Pop-Location}
    & $dotnet build HomeKTV.sln -c $Configuration --nologo --no-restore
    if($LASTEXITCODE -ne 0){throw '.NET 解决方案构建失败。'}
    $output=Join-Path $repoRoot "src/HomeKTV.App/bin/$Configuration/net10.0-windows/win-x64/Web";if(Test-Path $output){Remove-Item -LiteralPath $output -Recurse -Force};Copy-Item -LiteralPath (Join-Path $repoRoot 'src/HomeKTV.Web/dist') -Destination $output -Recurse
    Write-Host "[HomeKTV] $Configuration 构建成功。" -ForegroundColor Green
}finally{Pop-Location}
