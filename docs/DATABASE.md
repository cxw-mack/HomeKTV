# 数据库设计

数据库位于便携根目录 `Data/HomeKTV.db`，由 Microsoft.Data.Sqlite 打开，不需要安装数据库服务。启动时执行幂等 DDL 和 `PRAGMA quick_check`。

主要表包括 `Songs`、`Singers`、`SongSingers`、`Categories`、`Favorites`、`PlayHistory`、`QueueItems`、`GuestSessions`、`ApplicationSettings`、`ImportTasks`、`MediaInspections`、`DatabaseBackups` 和 `SchemaMigrations`。歌曲标题、歌手、拼音和首字母有索引；非空 SHA-256 使用唯一索引防重。

所有写入通过 `HomeKtvDatabase.WriteAsync` 的单进程信号量和短事务串行化。连接使用 `foreign_keys=ON`、`busy_timeout=5000`、`journal_mode=TRUNCATE` 和 `synchronous=FULL`，并关闭连接池，优先保证移动存储安全关闭与快速失败。

媒体列只允许相对于 `AppContext.BaseDirectory` 的路径，例如 `Media/MV/Beyond - 海阔天空.mp4`。存储时统一使用 `/`，解析时基于当前便携根目录，因此盘符变化不会影响播放。

在线备份使用 SQLite Backup API，备份后执行完整性检查并记录到 `DatabaseBackups`。恢复前自动创建当前库备份，对所选文件执行检查后通过临时文件替换。配置 JSON 损坏时保留 `Settings.corrupt-时间.json` 并恢复默认值。

