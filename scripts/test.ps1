[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop';$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$results=Join-Path $repoRoot 'TestResults'
$bundledDotNet=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-tools/dotnet10/dotnet.exe';$dotnet=if($env:HOMEKTV_DOTNET){$env:HOMEKTV_DOTNET}elseif(Test-Path $bundledDotNet){$bundledDotNet}elseif(Get-Command dotnet -ErrorAction SilentlyContinue){(Get-Command dotnet).Source}else{''}
New-Item -ItemType Directory -Force -Path $results|Out-Null
Push-Location $repoRoot
try{
    Push-Location 'src/HomeKTV.Web';try{& npm run build;if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}}finally{Pop-Location}
    & $dotnet test HomeKTV.sln -c $Configuration --nologo --logger trx --results-directory $results
    if($LASTEXITCODE -ne 0){throw '自动化测试失败。'}
    Write-Host "[HomeKTV] 全部测试通过；报告：$results" -ForegroundColor Green
}finally{Pop-Location}
