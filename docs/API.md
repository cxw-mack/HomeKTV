# 手机点歌 API

默认地址为 `http://局域网IPv4:16888`；端口占用时依次尝试后续端口。局域网模式绑定所有 IPv4，本机模式仅绑定 loopback。

| 方法 | 路径 | 用途 |
|---|---|---|
| GET | `/health` | 服务健康与实际端口 |
| POST | `/api/session` | 用昵称创建本次聚会临时会话 |
| GET | `/api/state` | 当前播放与完整队列；已认证时返回个人化 `isMine` |
| GET | `/api/songs?q=&language=&limit=` | 歌名、歌手、拼音、首字母和别名搜索 |
| GET | `/api/queue` | 查看公平排序后的队列 |
| POST | `/api/queue` | 已认证普通会话点歌 |
| DELETE | `/api/queue/{id}` | 删除本人尚未播放的歌曲 |
| POST | `/api/queue/{id}/move` | 仅在本人待播歌曲之间上移/下移 |
| POST | `/api/songs/{id}/favorite` | 收藏/取消收藏 |
| POST | `/api/admin/queue/{id}/pin` | 管理员置顶，需 `X-Admin-Pin` |
| POST | `/api/admin/queue/{id}/move` | 管理员调整任意待播歌曲，需 `X-Admin-Pin` |
| DELETE | `/api/admin/queue` | 管理员清空待播，需 `X-Admin-Pin` |
| WebSocket | `/hub` | SignalR 队列和播放状态同步 |

除公开搜索/状态读取外，普通会话请求必须同时携带 `X-HomeKTV-Session` 和 `X-HomeKTV-Token`；队列 JSON 不暴露会话 ID 或 token。SignalR 事件为 `queueChanged` 和 `playbackChanged`。客户端支持初次连接失败重试、断线自动重连，并在恢复后重新获取个人化快照。普通会话不能切歌、清空、置顶、调主音量或删除他人歌曲；管理员 PIN 使用恒定时间字节比较。
