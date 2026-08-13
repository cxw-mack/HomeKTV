# HomeKTV Android

Android TV box, phone, and tablet client for local HomeKTV media libraries.

The app is a standalone native Android rewrite. Select a song-library folder from the device or a USB drive. Each song folder may contain an MV/original track, an accompaniment track, an LRC/TXT lyric file, and a cover image. The folder permission is persisted through Android's Storage Access Framework, so no broad storage permission is required.

Included workflows:

- Folder scanning with automatic original/accompaniment/lyrics association.
- Song search, singer sorting, favorites, lyrics, MV/audio filters, and a playback queue.
- ExoPlayer playback with position-preserving original/accompaniment switching.
- Accompaniment volume defaults to 40% of the original volume.
- TV remote focus navigation through the Leanback launcher entry point.
- Responsive layout for portrait phones, tablets, landscape tablets, and TV screens.

## Build

The project keeps its JDK, Android SDK, and Gradle distribution under `.tooling` for a reproducible local build. Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-apk.ps1
```

Output:

`dist\HomeKTV-Android-v1.0.0.apk`

The APK is locally signed for direct installation on TV boxes, phones, and tablets. Enable installation from unknown sources on the device, install the APK, then choose the media folder inside the app.
