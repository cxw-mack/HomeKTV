# HomeKTV 工程约定

## 项目目标

HomeKTV 是面向家庭聚会的离线 Windows 11 x64 KTV 点歌系统。主屏运行 WPF 管理台，第二显示器播放 MV，同一局域网内的手机通过内嵌 Web 服务点歌。最终交付必须是可复制到移动硬盘、无需预装 .NET、Node.js、VLC、FFmpeg 或数据库的便携包。

## 目录结构

- `src/HomeKTV.App`：WPF 入口、MVVM 管理台和大屏窗口。
- `src/HomeKTV.Core`：领域模型、队列、便携路径抽象和接口。
- `src/HomeKTV.Infrastructure`：SQLite、配置、备份、导入、日志和媒体检测。
- `src/HomeKTV.Player`：LibVLCSharp 播放服务。
- `src/HomeKTV.Library`：歌曲搜索、文件名解析和导入编排。
- `src/HomeKTV.Lyrics`：LRC 解析与同步定位。
- `src/HomeKTV.Server`：Minimal API、SignalR、会话与权限。
- `src/HomeKTV.Web`：Vue 3 + TypeScript 手机端。
- `tests`：单元测试与集成测试。
- `scripts`：引导、构建、测试、发布和便携性校验。
- `docs`：架构、数据库、API、部署与测试文档。

## 常用命令

```powershell
./scripts/bootstrap.ps1
./scripts/build.ps1
./scripts/test.ps1
./scripts/publish-portable.ps1
./scripts/verify-portable.ps1
```

开发机未把 .NET 10 加入 PATH 时，脚本会从当前 Windows 用户目录发现 `.codex-tools/dotnet10/dotnet.exe`，也可通过 `HOMEKTV_DOTNET` 指定。

## 便携性约束

- 运行时根目录只能来自 `AppContext.BaseDirectory`，测试可通过显式构造函数注入替代根目录。
- 数据库只保存相对于便携根目录、使用 `/` 分隔符的媒体路径。
- 核心数据只写入 `Data`、`Media`、`Logs`、`Runtime`、`Web` 和 `Licenses`，禁止写入 AppData 或注册表。
- 不能写死开发机盘符、绝对媒体路径、显示器 ID 或音频设备 ID。
- 所有数据库写操作经串行写入门控制，并采用适合可移动存储的 SQLite 设置。
- 发布包断网可启动，Web 资源不使用 CDN，主程序无需管理员权限。

## 构建和测试标准

- .NET 与 npm 依赖使用精确正式版本；禁止 Preview、Alpha、Beta。
- `dotnet build`、`npm run build` 和全部测试必须通过。
- 新增领域行为应有单元测试；API、数据库和恢复流程应有集成测试。
- 修复构建或测试问题时不得删除需求功能以规避错误。
- 每个关键阶段创建 Git commit，提交前保持工作树可构建。

## 完成标准

`dist/HomeKTV-Portable-win-x64/HomeKTV.exe` 可在 Windows 11 x64 独立启动；能够初始化/恢复数据库、导入并搜索歌曲、排队和播放、同步 LRC、运行手机点歌页与 SignalR、在第二显示器全屏；发布目录移动或盘符变化后仍可使用，并生成 `dist/HomeKTV-Portable-win-x64.zip`。
