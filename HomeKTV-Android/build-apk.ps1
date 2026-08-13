$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '.')).Path
$toolRoot = Join-Path $projectRoot '.tooling'
$javaHome = Join-Path $toolRoot 'jdk-17.0.20+8'
$sdkRoot = Join-Path $toolRoot 'android-sdk'
$gradle = Join-Path $toolRoot 'gradle-8.10.2\bin\gradle.bat'

if (-not (Test-Path (Join-Path $javaHome 'bin\java.exe'))) { throw "Missing local JDK: $javaHome" }
if (-not (Test-Path (Join-Path $sdkRoot 'platforms\android-35\android.jar'))) { throw "Missing Android SDK platform 35: $sdkRoot" }
if (-not (Test-Path $gradle)) { throw "Missing Gradle: $gradle" }

$env:JAVA_HOME = $javaHome
$env:ANDROID_HOME = $sdkRoot
$env:ANDROID_SDK_ROOT = $sdkRoot
$env:Path = (Join-Path $javaHome 'bin') + ';' + $env:Path
$sdkProperty = $sdkRoot.Replace('\', '\\').Replace(':', '\:')
Set-Content -LiteralPath (Join-Path $projectRoot 'local.properties') -Value "sdk.dir=$sdkProperty" -Encoding ASCII

Push-Location $projectRoot
try {
    & $gradle ':app:testReleaseUnitTest' ':app:lintRelease' ':app:assembleRelease' '--stacktrace' '--no-daemon'
    if ($LASTEXITCODE -ne 0) { throw "Gradle build failed with exit code $LASTEXITCODE" }
    $output = Join-Path $projectRoot 'dist'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $apk = Join-Path $projectRoot 'app\build\outputs\apk\release\app-release.apk'
    Copy-Item -LiteralPath $apk -Destination (Join-Path $output 'HomeKTV-Android-v1.0.0.apk') -Force
    Get-Item -LiteralPath (Join-Path $output 'HomeKTV-Android-v1.0.0.apk') | Select-Object FullName, Length, LastWriteTime
} finally {
    Pop-Location
}
