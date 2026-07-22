# 便携发布

运行：

```powershell
./scripts/bootstrap.ps1
./scripts/publish-portable.ps1
./scripts/verify-portable.ps1
```

发布参数固定为 Release、win-x64、SelfContained、PublishSingleFile、PublishTrimmed=false、DebugType=embedded。主托管程序合并为 `HomeKTV.exe`；`IncludeNativeLibrariesForSelfExtract=false`，因为 LibVLC 插件必须保持目录结构，不能嵌入单文件。发布脚本只取 VideoLAN 包中的 win-x64 原生目录到 `Runtime/LibVLC`，FFmpeg 8.0.1 通过固定 URL 和 SHA-256 获取到 `Runtime/FFmpeg`。

校验脚本检查入口、数据/媒体/日志/许可证目录、VLC 核心和至少 20 个插件、FFmpeg/FFprobe、Web 静态资源和配置绝对盘符；然后把整个目录复制到系统临时路径，从复制后的 `HomeKTV.exe --health-check` 初始化并打开数据库，确保不依赖源码或开发目录。

最终产物：

- `dist/HomeKTV-Portable-win-x64/`
- `dist/HomeKTV-Portable-win-x64.zip`

普通启动不请求管理员权限。`Configure-Firewall.ps1` 是唯一可能提升权限的独立操作，它先解释并要求明确输入 `YES`，且仅添加当前程序、TCP 端口、专用网络配置文件的规则。
