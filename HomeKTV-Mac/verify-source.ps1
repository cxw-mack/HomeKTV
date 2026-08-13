$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$required = @(
    'Package.swift',
    'Info.plist',
    'build-macos.sh',
    'Sources/HomeKTVMac/HomeKTVMacApp.swift',
    'Sources/HomeKTVMac/KTVStore.swift',
    'Sources/HomeKTVMac/LibraryScanner.swift',
    'Sources/HomeKTVMac/LyricsParser.swift',
    'Sources/HomeKTVMac/MainView.swift',
    'Tests/HomeKTVMacTests/HomeKTVMacTests.swift'
)
foreach ($item in $required) {
    if (-not (Test-Path (Join-Path $root $item))) { throw "Missing required file: $item" }
}
$source = (Get-ChildItem (Join-Path $root 'Sources') -Recurse -Filter *.swift | ForEach-Object {
    Get-Content $_.FullName -Raw -Encoding utf8
}) -join "`n"
if ($source -match 'LibVLC|WindowsForms|WPF|\.dll|\.exe') { throw 'Windows-specific dependency found in Mac source.' }
if ($source -notmatch 'AVPlayer' -or $source -notmatch 'LibraryScanner' -or $source -notmatch 'LyricsParser') { throw 'Mac playback, scanner, or lyric implementation is missing.' }
if ($source -notmatch 'NSHostingView' -or $source -notmatch 'accompanimentVolume: Float = 0\.4') { throw 'Dedicated display window or accompaniment volume policy is missing.' }
if ((Get-Content (Join-Path $root 'build-macos.sh') -Raw -Encoding utf8) -notmatch 'lipo -create') { throw 'Universal Mac build step is missing.' }
if (Get-ChildItem $root -Recurse -File -Include *.mp3,*.mp4,*.mkv,*.lrc,*.sqlite,*.db -ErrorAction SilentlyContinue) { throw 'Local music or database data must not be included.' }
Write-Host 'Source layout and platform dependency checks passed.'
