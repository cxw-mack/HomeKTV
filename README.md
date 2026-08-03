# HomeKTV

HomeKTV 是面向 Windows 11 x64 的家庭 KTV 系统。桌面端负责歌库、队列和大屏播放，手机端通过同一局域网扫码进入点歌台，无需安装 App。

## 功能

- 导入 MP4、MKV、AVI、MOV、WebM 等视频，以及 MP3、FLAC、WAV、M4A、AAC、OGG、OPUS 等音频。
- 为歌曲单独导入音频或视频伴奏，并在原唱与伴唱之间保持统一时间线。
- 导入带时间标签的 LRC/TXT 歌词，在大屏显示双行交替和渐进高亮的 KTV 歌词。
- 纯音频播放时随机显示歌曲图片、封面或本地默认幻灯片。
- 手机端支持点歌、队列、暂停、重唱、切歌、原唱/伴唱、歌词开关和音量控制。
- 支持 Android 和 iPhone 系统相机扫码加入，普通手机用户不需要管理员权限。
- 数据库、媒体和配置使用相对路径，可整体复制到移动硬盘运行。

## 技术栈

- .NET 10、WPF、SQLite
- LibVLCSharp、NAudio、FFmpeg
- ASP.NET Core、SignalR
- Vue 3、TypeScript、Vite

## 构建

首次准备依赖：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
```

运行完整测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1 -Configuration Release
```

生成 Windows x64 自包含便携版：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

输出位于 `dist/HomeKTV-Portable-win-x64` 和 `dist/HomeKTV-Portable-win-x64.zip`。

## 媒体与版权

仓库和发布包不包含商业歌曲、MV 或用户歌库。请仅导入你合法拥有或获授权使用的媒体，并遵守所在地版权法律。第三方组件信息见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
