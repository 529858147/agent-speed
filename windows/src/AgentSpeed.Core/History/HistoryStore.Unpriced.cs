using AgentSpeed.Core.Unpriced;

namespace AgentSpeed.Core.History;

internal sealed partial class HistoryStore
{
    /// <summary>
    /// The unpriced facts of every stored contribution (METRICS 9.10), created whenever the store is opened; the schema
    /// version does not change for it, so older builds still open the file and ignore it.
    /// </summary>
    private const string UnpricedSchema = """
        CREATE TABLE IF NOT EXISTS contribution_unpriced (
            file_id INTEGER NOT NULL,
            day TEXT NOT NULL,
            source TEXT NOT NULL,
            model TEXT NOT NULL,
            provider TEXT NOT NULL,
            fast INTEGER NOT NULL,
            requests INTEGER NOT NULL,
            version INTEGER NOT NULL,
            PRIMARY KEY (file_id, day, source, model, provider, fast)) WITHOUT ROWID;
        """;

    /// <summary>Reads every contribution's facts: its valid side rows, else derived from its models (METRICS 9.10 <i>Reading</i>).</summary>
    private void LoadUnpriced(Dictionary<long, StoredFile> files)
    {
        var side = new Dictionary<(long File, DayKey Day), List<UnpricedSideRow>>();
        using (var rows = _connection.Prepare(
            "SELECT file_id, day, source, model, provider, fast, requests, version FROM contribution_unpriced"))
        {
            while (rows.Step())
            {
                if (!files.ContainsKey(rows.GetInt64(0)) || Key(rows.GetString(1), rows.GetString(2)) is not { } day)
                {
                    continue;
                }

                var key = (rows.GetInt64(0), day);
                if (!side.TryGetValue(key, out var list))
                {
                    side[key] = list = [];
                }

                var provider = rows.GetString(4);
                list.Add(new UnpricedSideRow(rows.GetString(3) ?? string.Empty, string.IsNullOrEmpty(provider) ? null : provider,
                    rows.GetInt64(5) != 0, rows.GetInt64(6), (int)Math.Clamp(rows.GetInt64(7), int.MinValue, int.MaxValue)));
            }
        }

        foreach (var (id, file) in files)
        {
            foreach (var (day, aggregate) in file.Days)
            {
                var reading = UnpricedReading.Read(
                    file.Version,
                    aggregate.Requests,
                    aggregate.Cost?.UnpricedRequests,
                    aggregate.Models.Select(m => KeyValuePair.Create(m.Key, m.Value.Cost?.UnpricedRequests)),
                    side.TryGetValue((id, day), out var rows) ? rows : []);
                aggregate.SetUnpriced(reading);
            }
        }
    }

    /// <summary>Replaces a file's side rows by the facts of its contributions, inside the caller's transaction.</summary>
    private void SaveUnpriced(StoredFile file)
    {
        using var delete = _connection.Prepare("DELETE FROM contribution_unpriced WHERE file_id = ?1");
        delete.Bind(1, file.Id).Run();
        using var insert = _connection.Prepare(
            "INSERT INTO contribution_unpriced (file_id, day, source, model, provider, fast, requests, version) " +
            "VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8) ON CONFLICT(file_id, day, source, model, provider, fast) " +
            "DO UPDATE SET requests = requests + excluded.requests");
        foreach (var (day, aggregate) in file.Days)
        {
            foreach (var (fact, requests) in aggregate.Unpriced)
            {
                if (requests < 1)
                {
                    continue;
                }

                insert.Bind(1, file.Id).Bind(2, day.Day).Bind(3, SourceNames.Of(day.Source)).Bind(4, fact.Model)
                    .Bind(5, fact.Provider ?? string.Empty).Bind(6, fact.Fast ? 1L : 0L).Bind(7, requests).Bind(8, file.Version).Run();
            }
        }
    }
}
