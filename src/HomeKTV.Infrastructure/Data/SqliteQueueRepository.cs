using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;

namespace HomeKTV.Infrastructure.Data;

public sealed class SqliteQueueRepository(HomeKtvDatabase database) : IQueueRepository
{
    public Task<IReadOnlyList<QueueItem>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<QueueItem>>(async (connection, ct) =>
        {
            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT q.Id,q.SongId,q.GuestSessionId,q.RequestedBy,q.RequestedAt,q.Position,q.IsPinned,q.State,q.ErrorMessage,
                       s.Title,s.ArtistDisplayName,s.VideoRelativePath,s.LyricRelativePath,s.LyricOffsetMs,s.IsAvailable,
                       s.OriginalAudioTrack,s.AccompanimentAudioTrack,s.DefaultAudioMode
                FROM QueueItems q JOIN Songs s ON s.Id=q.SongId
                WHERE q.State IN (0,1,2,3) ORDER BY q.IsPinned DESC,q.Position,q.RequestedAt,q.Id;
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            var result = new List<QueueItem>();
            while (await reader.ReadAsync(ct))
            {
                result.Add(new QueueItem
                {
                    Id=reader.GetInt64(0), SongId=reader.GetInt64(1), GuestSessionId=reader.GetString(2), RequestedBy=reader.GetString(3),
                    RequestedAt=DateTimeOffset.Parse(reader.GetString(4)), Position=reader.GetInt64(5), IsPinned=reader.GetBoolean(6), State=(QueueItemState)reader.GetInt32(7),
                    ErrorMessage=reader.IsDBNull(8)?null:reader.GetString(8), Song=new Song { Id=reader.GetInt64(1), Title=reader.GetString(9), ArtistDisplayName=reader.GetString(10),
                        VideoRelativePath=reader.GetString(11), LyricRelativePath=reader.IsDBNull(12)?null:reader.GetString(12), LyricOffsetMs=reader.GetInt32(13), IsAvailable=reader.GetBoolean(14),
                        OriginalAudioTrack=reader.IsDBNull(15)?null:reader.GetInt32(15),AccompanimentAudioTrack=reader.IsDBNull(16)?null:reader.GetInt32(16),DefaultAudioMode=(AudioMode)reader.GetInt32(17) }
                });
            }
            return result;
        }, cancellationToken);

    public Task<QueueItem> EnqueueAsync(long songId, string sessionId, string requestedBy, CancellationToken cancellationToken = default) =>
        database.WriteAsync(async (connection, transaction, ct) =>
        {
            var command=connection.CreateCommand(); command.Transaction=transaction;
            command.CommandText="""
                INSERT INTO QueueItems(SongId,GuestSessionId,RequestedBy,RequestedAt,Position,IsPinned,State)
                SELECT $song,$session,$name,$now,COALESCE(MAX(Position),0)+1,0,0 FROM QueueItems
                RETURNING Id,Position;
                """;
            var now=DateTimeOffset.UtcNow; command.Parameters.AddWithValue("$song",songId); command.Parameters.AddWithValue("$session",sessionId); command.Parameters.AddWithValue("$name",requestedBy); command.Parameters.AddWithValue("$now",now.ToString("O"));
            await using var reader=await command.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);var id=reader.GetInt64(0);var position=reader.GetInt64(1);
            return new QueueItem { Id=id,SongId=songId,GuestSessionId=sessionId,RequestedBy=requestedBy,RequestedAt=now,Position=position };
        },cancellationToken);

    public Task<bool> RemoveAsync(long queueItemId, string? ownerSessionId, bool administrator, CancellationToken cancellationToken = default) =>
        database.WriteAsync(async (connection, transaction, ct) => { var c=connection.CreateCommand(); c.Transaction=transaction;
            c.CommandText="UPDATE QueueItems SET State=7 WHERE Id=$id AND State=0 AND ($admin=1 OR GuestSessionId=$owner);";
            c.Parameters.AddWithValue("$id",queueItemId); c.Parameters.AddWithValue("$admin",administrator); c.Parameters.AddWithValue("$owner",ownerSessionId??string.Empty);
            return await c.ExecuteNonQueryAsync(ct)>0; },cancellationToken);

    public Task<bool> SetPinnedAsync(long queueItemId, bool pinned, CancellationToken cancellationToken = default) =>
        database.WriteAsync(async (connection, transaction, ct) => { var c=connection.CreateCommand(); c.Transaction=transaction;c.CommandText="UPDATE QueueItems SET IsPinned=$pinned WHERE Id=$id AND State=0;";c.Parameters.AddWithValue("$pinned",pinned);c.Parameters.AddWithValue("$id",queueItemId);return await c.ExecuteNonQueryAsync(ct)>0;},cancellationToken);

    public Task<bool> MoveAsync(long queueItemId, int direction, string? ownerSessionId, bool administrator, CancellationToken cancellationToken = default) =>
        database.WriteAsync(async (connection, transaction, ct) =>
        {
            if(direction is not -1 and not 1) throw new ArgumentOutOfRangeException(nameof(direction),"方向只能是 -1 或 1。");
            var current=connection.CreateCommand();current.Transaction=transaction;current.CommandText="SELECT Position,GuestSessionId FROM QueueItems WHERE Id=$id AND State=0 AND ($admin=1 OR GuestSessionId=$owner);";current.Parameters.AddWithValue("$id",queueItemId);current.Parameters.AddWithValue("$admin",administrator);current.Parameters.AddWithValue("$owner",ownerSessionId??string.Empty);
            await using var reader=await current.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))return false;var position=reader.GetInt64(0);var session=reader.GetString(1);await reader.DisposeAsync();
            var adjacent=connection.CreateCommand();adjacent.Transaction=transaction;adjacent.CommandText=direction<0
                ? "SELECT Id,Position FROM QueueItems WHERE State=0 AND Position<$position AND ($admin=1 OR GuestSessionId=$session) ORDER BY Position DESC LIMIT 1;"
                : "SELECT Id,Position FROM QueueItems WHERE State=0 AND Position>$position AND ($admin=1 OR GuestSessionId=$session) ORDER BY Position LIMIT 1;";
            adjacent.Parameters.AddWithValue("$position",position);adjacent.Parameters.AddWithValue("$admin",administrator);adjacent.Parameters.AddWithValue("$session",session);
            await using var otherReader=await adjacent.ExecuteReaderAsync(ct);if(!await otherReader.ReadAsync(ct))return false;var otherId=otherReader.GetInt64(0);var otherPosition=otherReader.GetInt64(1);await otherReader.DisposeAsync();
            var swap=connection.CreateCommand();swap.Transaction=transaction;swap.CommandText="UPDATE QueueItems SET Position=CASE WHEN Id=$id THEN $otherPosition ELSE $position END WHERE Id IN ($id,$otherId);";swap.Parameters.AddWithValue("$id",queueItemId);swap.Parameters.AddWithValue("$otherId",otherId);swap.Parameters.AddWithValue("$position",position);swap.Parameters.AddWithValue("$otherPosition",otherPosition);return await swap.ExecuteNonQueryAsync(ct)==2;
        },cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) => database.WriteAsync(async (connection,transaction,ct)=>{var c=connection.CreateCommand();c.Transaction=transaction;c.CommandText="UPDATE QueueItems SET State=7 WHERE State IN (0,1,3);";await c.ExecuteNonQueryAsync(ct);return 0;},cancellationToken);

    public Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default) => database.WriteAsync(async (connection,transaction,ct)=>{var c=connection.CreateCommand();c.Transaction=transaction;c.CommandText="UPDATE QueueItems SET State=0,ErrorMessage='程序异常退出后已恢复为待播' WHERE State IN (1,2,3);";return await c.ExecuteNonQueryAsync(ct);},cancellationToken);

    public Task SetStateAsync(long queueItemId, QueueItemState state, string? error = null, CancellationToken cancellationToken = default) => database.WriteAsync(async (connection,transaction,ct)=>{var c=connection.CreateCommand();c.Transaction=transaction;c.CommandText="UPDATE QueueItems SET State=$state,ErrorMessage=$error WHERE Id=$id;";c.Parameters.AddWithValue("$state",(int)state);c.Parameters.AddWithValue("$error",(object?)error??DBNull.Value);c.Parameters.AddWithValue("$id",queueItemId);await c.ExecuteNonQueryAsync(ct);return 0;},cancellationToken);
}
