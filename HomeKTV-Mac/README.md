# HomeKTV Mac

HomeKTV 的原生 macOS 版本，支持 macOS 13 Ventura 及以上系统。它不依赖 Windows 版、VLC、.NET 或本地服务，歌曲数据也不会打进安装包。

## 功能

- 选择歌曲根目录后递归扫描，每首歌一个文件夹即可。
- 自动识别原唱 MV/音频、伴奏、`.lrc` / `.txt` 歌词和封面图片。
- 支持歌名、歌手、文件夹搜索，收藏和点歌队列。
- 原唱/伴奏切换保留播放进度；伴奏音量默认按原唱的 40% 输出。
- 大屏是独立无边框窗口，优先显示在外接电视或第二显示器，可从主窗口准确关闭。
- LRC 歌词使用蓝色主行和黑色阴影，主行字号为 80。

## 在 Mac 上生成安装包

1. 安装 Xcode 15 或更高版本，并首次打开接受许可。
2. 将整个 `HomeKTV-Mac` 文件夹复制到 Mac。
3. 在“终端”执行：

```bash
cd /你的路径/HomeKTV-Mac
bash build-macos.sh
```

生成的文件在 `dist/HomeKTV-Mac.app` 和 `dist/HomeKTV-Mac-macos.zip`。这是同时兼容 Apple Silicon 和 Intel 的通用包。将 `.app` 拖入 `/Applications` 后即可安装。该本地构建采用临时签名，不需要 Apple Developer 账号；首次从下载位置打开时，在 Finder 中右键选择“打开”一次即可。

## 歌曲目录格式

每一首歌使用一个文件夹，例如：

```text
Music/
  周杰伦 - 七里香/
    周杰伦 - 七里香 原唱.mp4
    周杰伦 - 七里香 伴奏.mp3
    周杰伦 - 七里香 原唱.lrc
    cover.jpg
```

可识别 `原唱`、`原版`、`伴奏`、`消音`、`纯音乐`、`instrumental`、`karaoke`、`backing track` 等命名。没有歌词文件的歌曲仍可正常导入和播放。

仓库和构建输出均不包含歌曲、MV、歌词或已有曲库数据。
