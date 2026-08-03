# HomeKTV 架构

## 总览

HomeKTV 采用桌面主进程内组合的模块化架构。WPF 是主生命周期宿主；ASP.NET Core WebApplication 在后台端口启动；SQLite 保存歌曲库和统一队列；LibVLC 负责 MV、纯音频以及外部伴奏同步播放；Vue 静态资源由内嵌服务器从便携包 `Web` 目录提供。FFmpeg 用于媒体检查、转码和从视频伴奏中提取音轨。

```text
WPF 管理台 ─┐
第二屏窗口 ─┼─ 应用服务 / 事件 ─ Core ─ SQLite
Vue 手机端 ─┘        │          │
       SignalR/API ─ Server      ├─ Library / Lyrics
                                 └─ Player / LibVLC / Slideshow / FFmpeg
```

## 依赖方向

- `Core` 不依赖 UI、数据库或媒体库。
- `Lyrics` 与 `Library` 仅依赖 `Core`。
- `Infrastructure` 实现 `Core` 定义的持久化和系统接口。
- `Player` 实现播放接口并封装 LibVLCSharp。
- `Server` 组合应用服务并暴露受限 API/SignalR。
- `App` 是唯一组合根，负责启动、停止和全局异常处理。

## 便携根目录

生产环境使用 `PortablePaths.FromBaseDirectory()`，其根为 `AppContext.BaseDirectory`。所有子路径通过该值组合并经过边界校验。数据库保存规范化相对路径，解析时使用当前根，因此目录复制、移动和盘符变化不会破坏媒体引用。

## 数据一致性与可移动存储

SQLite 启用外键和 busy timeout。所有应用内读写都经进程内操作门，其中写入再串行化；恢复会取得独占门，避免服务器请求与文件替换竞争。启动执行 `PRAGMA quick_check`。备份使用 SQLite 在线备份 API 并验证结果；恢复前先备份现库，并以临时文件替换降低拔盘风险。启动发现损坏时可恢复最新有效备份，原库保留到 `Data/Corrupt`。

## 播放和队列

队列支持 `Video`、`Audio`、`VideoWithExternalAudio` 与 `AudioWithSlideshow` 混排。置顶项目始终优先，同一用户内部保持相对顺序。`MediaPlaybackCoordinator` 统一选择场景；纯音频启动按歌曲配置计时的幻灯片，暂停、继续、定位、重唱和切歌同步；外部音轨每 500ms 检查漂移，超过阈值自动纠正，丢失时回退 MV 内嵌音频。LibVLC 仅由主播放器和独立审核试听播放器持有，窗口关闭和应用退出都会释放媒体、播放器与核心实例。

## 独立伴奏

单曲导入可附带音频或视频伴奏。音频按原格式复制到便携媒体目录；视频由 FFmpeg 提取第一条音轨为无损 FLAC。视频歌曲播放时，外部伴奏与 MV 使用同一时间线；切回原唱时伴奏静音但继续运行，再切伴奏不重新定位。歌曲可保存毫秒级外部音频偏移。

## 网络和安全

默认端口 16888，冲突时按顺序探测备用端口并以实际绑定结果为准。仅本机模式绑定 loopback，局域网模式绑定所有 IPv4 接口。临时会话证书只保存 SHA-256 哈希，有效期 12 小时，数量限制为 64；普通会话只能管理自己的待播项目。管理 API 要求本地 PIN。Web 端没有 CDN 或远程启动依赖。防火墙脚本是独立、显式确认的管理员操作。

## 恢复与降级

- 配置 JSON 损坏时改名保存并生成默认配置。
- 数据库检查失败时给出恢复路径并保留损坏文件。
- 缺少 FFmpeg 只禁用检测/转码，不阻止点歌和播放。
- 没有第二显示器时大屏退化为主屏窗口模式。
- 媒体缺失或解码失败时记录失败并推进下一首。
