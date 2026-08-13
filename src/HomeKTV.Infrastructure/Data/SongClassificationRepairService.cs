using HomeKTV.Library;

namespace HomeKTV.Infrastructure.Data;

public sealed class SongClassificationRepairService(HomeKtvDatabase database)
{
    public Task<int> RepairAsync(CancellationToken cancellationToken = default) =>
        database.WriteAsync(async (connection, transaction, ct) =>
        {
            var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT Id, Title, ArtistDisplayName, Language, CategoryId FROM Songs;";
            var repairs = new List<(long Id, string Language, long CategoryId)>();

            await using (var reader = await select.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetInt64(0);
                    var title = reader.GetString(1);
                    var artist = reader.GetString(2);
                    var currentLanguage = reader.IsDBNull(3) ? SongLanguageClassifier.Other : reader.GetString(3);
                    var normalized = SongLanguageClassifier.Normalize(currentLanguage);
                    var language = normalized == SongLanguageClassifier.Other
                        ? SongLanguageClassifier.Infer(artist, title)
                        : normalized;
                    var categoryId = SongLanguageClassifier.CategoryIdFor(language);
                    long? currentCategoryId = reader.IsDBNull(4) ? null : reader.GetInt64(4);

                    if (language != normalized || currentLanguage != normalized || currentCategoryId != categoryId)
                        repairs.Add((id, language, categoryId));
                }
            }

            foreach (var repair in repairs)
            {
                var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE Songs SET Language=$language, CategoryId=$category WHERE Id=$id;";
                update.Parameters.AddWithValue("$language", repair.Language);
                update.Parameters.AddWithValue("$category", repair.CategoryId);
                update.Parameters.AddWithValue("$id", repair.Id);
                await update.ExecuteNonQueryAsync(ct);
            }

            return repairs.Count;
        }, cancellationToken);
}
