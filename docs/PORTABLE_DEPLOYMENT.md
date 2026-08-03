# 便携发布

运行：

```powershell
./scripts/bootstrap.ps1
./scripts/publish-portable.ps1
./scripts/verify-portable.ps1
```

发布参数固定为 Release、win-x64、SelfContained、PublishSingleFile、PublishTrimmed=false。主托管程序合并为 `HomeKTV.exe`；LibVLC 保持插件目录结构。发布脚本复制固定并校验的 FFmpeg；目标电脑不需要系统 VLC、FFmpeg、Node.js 或 .NET。

校验脚本检查入口、数据/音频/幻灯片/日志/许可证目录、至少五张默认图片及许可证、VLC 核心和插件、FFmpeg/FFprobe、Web 静态资源和配置绝对盘符；然后把整个目录复制到系统临时路径，执行模拟断网、端口冲突、配置损坏、首次建库、FFmpeg 缺失降级及 MV/纯音频混合播放。

最终产物：

- `dist/HomeKTV-Portable-win-x64/`
- `dist/HomeKTV-Portable-win-x64.zip`

普通启动不请求管理员权限。`Configure-Firewall.ps1` 是唯一可能提升权限的独立操作，它先解释并要求明确输入 `YES`，且仅添加当前程序、TCP 端口、专用网络配置文件的规则。
