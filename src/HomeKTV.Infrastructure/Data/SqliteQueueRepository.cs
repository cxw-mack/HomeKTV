using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;

namespace HomeKTV.Infrastructure.Data;

public sealed class SqliteQueueRepository(HomeKtvDatabase database) : IQueueRepository
{
    public async Task<IReadOnlyList<QueueItem>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT q.Id,q.SongId,q.GuestSessionId,q.RequestedBy,q.RequestedAt,q.Position,q.IsPinned,q.State,q.ErrorMessage,
                   s.Title,s.ArtistDisplayName,s.VideoRelativePath,s.LyricRelativePath,s.LyricOffsetMs,s.IsAvailable
            FROM QueueItems q JOIN Songs s ON s.Id=q.SongId
            WHERE q.State IN (0,1,2,3) ORDER BY q.IsPinned DESC,q.Position,q.RequestedAt,q.Id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<QueueItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new QueueItem
            {
                Id=reader.GetInt64(0), SongId=reader.GetInt64(1), GuestSessionId=reader.GetString(2), RequestedBy=reader.GetString(3),
                RequestedAt=DateTimeOffset.Parse(reader.GetString(4)), Position=reader.GetInt64(5), IsPinned=reader.GetBoolean(6), State=(QueueItemState)reader.GetInt32(7),
                ErrorMessage=reader.IsDBNull(8)?null:reader.GetString(8), Song=new Song { Id=reader.GetInt64(1), Title=reader.GetString(9), ArtistDisplayName=reader.GetString(10),
                    VideoRelativePath=reader.GetString(11), LyricRelativePath=reader.IsDBNull(12)?null:reader.GetString(12), LyricOffsetMs=reader.GetInt32(13), IsAvailable=reader.GetBoolean(14) }
            });
        }
        return result;
    }

    public Task<QueueItem> EnqueueAsync(long songId, string sessionId, string requestedBy, CancellationToken cancellationToken = default) =>
        database.WriteAsync(async (connection, transaction, ct) =>
        {
            var command=connection.CreateCommand(); command.Transaction=transaction;
            command.CommandText="INSERT INTO QueueItems(SongId,GuestSessionId,RequestedBy,RequestedAt,Position,IsPinned,State) SELECT $song,$session,$name,$now,COALESCE(MAX(Position),0)+1,0,0 FROM QueueItems; SELECT last_insert_rowid();";
            var now=DateTimeOffset.UtcNow; command.Parameters.AddWithValue("$song",songId); command.Parameters.AddWithValue("$session",sessionId); command.Parameters.AddWithValue("$name",requestedBy); command.Parameters.AddWithValue("$now",now.ToString("O"));
            var id=Convert.ToInt64(await command.ExecuteScalarAsync(ct));
            return new QueueItem { Id=id,SongId=songId,GuestSessionId=sessionId,RequestedBy=requestedBy,RequestedAt=now,Position=id };
        },cancellationToken);

    public Task<bool> RemoveAsync(long queueItemId, string? ownerSessionId, bool administrator, CancellationToken cancellationToken = default) =>
        database.WriteAsync(async (connection, transaction, ct) => { var c=connection.CreateCommand(); c.Transaction=transaction;
            c.CommandText="UPDATE QueueItems SET State=7 WHERE Id=$id AND State=0 AND ($admin=1 OR GuestSessionId=$owner);";
            c.Parameters.AddWithValue("$id",queueItemId); c.Parameters.AddWithValue("$admin",administrator); c.Parameters.AddWithValue("$owner",ownerSessionId??string.Empty);
            return await c.ExecuteNonQueryAsync(ct)>0; },cancellationToken);

    public Task<bool> PinAsync(long queueItemId, CancellationToken cancellationToken = default) =>
        database.WriteAsync(async (connection, transaction, ct) => { var c=connection.CreateCommand(); c.Transaction=transaction; c.CommandText="UPDATE QueueItems SET IsPinned=1 WHERE Id=$id AND State=0;"; c.Parameters.AddWithValue("$id",queueItemId); return await c.ExecuteNonQueryAsync(ct)>0; },cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) => database.WriteAsync(async (connection,transaction,ct)=>{var c=connection.CreateCommand();c.Transaction=transaction;c.CommandText="UPDATE QueueItems SET State=7 WHERE State IN (0,1,3);";await c.ExecuteNonQueryAsync(ct);return 0;},cancellationToken);

    public Task SetStateAsync(long queueItemId, QueueItemState state, string? error = null, CancellationToken cancellationToken = default) => database.WriteAsync(async (connection,transaction,ct)=>{var c=connection.CreateCommand();c.Transaction=transaction;c.CommandText="UPDATE QueueItems SET State=$state,ErrorMessage=$error WHERE Id=$id;";c.Parameters.AddWithValue("$state",(int)state);c.Parameters.AddWithValue("$error",(object?)error??DBNull.Value);c.Parameters.AddWithValue("$id",queueItemId);await c.ExecuteNonQueryAsync(ct);return 0;},cancellationToken);
}

