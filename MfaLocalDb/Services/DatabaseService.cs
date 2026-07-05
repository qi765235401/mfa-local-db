using Microsoft.Data.Sqlite;
using MfaLocalDb.Models;

namespace MfaLocalDb.Services;

public sealed class DatabaseService
{
    private readonly string _databasePath;

    public DatabaseService(string databasePath)
    {
        _databasePath = databasePath;
    }

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                kind TEXT NOT NULL,
                region TEXT NOT NULL,
                name TEXT NOT NULL,
                title TEXT NOT NULL,
                source_url TEXT NOT NULL UNIQUE,
                content_text TEXT NOT NULL,
                content_html TEXT NOT NULL,
                synced_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_entries_kind_name ON entries(kind, name);
            CREATE INDEX IF NOT EXISTS idx_entries_region_name ON entries(region, name);
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<EntryListItem> SearchEntries(string kindFilter, string keyword)
    {
        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, kind, region, name, title, source_url, synced_at
            FROM entries
            WHERE (@kind = '' OR kind = @kind)
              AND (
                    @keyword = ''
                    OR name LIKE '%' || @keyword || '%'
                    OR title LIKE '%' || @keyword || '%'
                    OR content_text LIKE '%' || @keyword || '%'
                  )
            ORDER BY
                CASE
                    WHEN @keyword = '' THEN 100
                    WHEN name = @keyword THEN 0
                    WHEN title = @keyword THEN 1
                    WHEN name LIKE @keyword || '%' THEN 2
                    WHEN title LIKE @keyword || '%' THEN 3
                    WHEN name LIKE '%' || @keyword || '%' THEN 4
                    WHEN title LIKE '%' || @keyword || '%' THEN 5
                    ELSE 6
                END,
                length(name),
                CASE kind WHEN '国家' THEN 0 ELSE 1 END,
                CASE region
                    WHEN '亚洲' THEN 0
                    WHEN '非洲' THEN 1
                    WHEN '欧洲' THEN 2
                    WHEN '北美洲' THEN 3
                    WHEN '南美洲' THEN 4
                    WHEN '大洋洲' THEN 5
                    WHEN '国际和地区组织' THEN 6
                    ELSE 7
                END,
                name;
            """;
        command.Parameters.AddWithValue("@kind", kindFilter ?? string.Empty);
        command.Parameters.AddWithValue("@keyword", keyword ?? string.Empty);

        var result = new List<EntryListItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new EntryListItem
            {
                Id = reader.GetInt64(0),
                Kind = reader.GetString(1),
                Region = reader.GetString(2),
                Name = reader.GetString(3),
                Title = reader.GetString(4),
                SourceUrl = reader.GetString(5),
                SyncedAt = reader.GetString(6),
            });
        }

        return result;
    }

    public EntryDetail? GetEntry(long id)
    {
        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, kind, region, name, title, source_url, synced_at, content_text, content_html
            FROM entries
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new EntryDetail
        {
            Id = reader.GetInt64(0),
            Kind = reader.GetString(1),
            Region = reader.GetString(2),
            Name = reader.GetString(3),
            Title = reader.GetString(4),
            SourceUrl = reader.GetString(5),
            SyncedAt = reader.GetString(6),
            ContentText = reader.GetString(7),
            ContentHtml = reader.GetString(8),
        };
    }

    public int GetEntryCount()
    {
        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM entries;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public string GetLatestSyncedAt()
    {
        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(synced_at), '') FROM entries;";
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    public IReadOnlySet<string> GetCountryNames()
    {
        using var connection = OpenConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM entries
            WHERE kind = '国家';
            """;

        var names = new HashSet<string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public void UpsertEntries(IEnumerable<ScrapedEntry> entries)
    {
        using var connection = OpenConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var entry in entries)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO entries (kind, region, name, title, source_url, content_text, content_html, synced_at)
                VALUES (@kind, @region, @name, @title, @source_url, @content_text, @content_html, @synced_at)
                ON CONFLICT(source_url) DO UPDATE SET
                    kind = excluded.kind,
                    region = excluded.region,
                    name = excluded.name,
                    title = excluded.title,
                    content_text = excluded.content_text,
                    content_html = excluded.content_html,
                    synced_at = excluded.synced_at;
                """;
            command.Parameters.AddWithValue("@kind", entry.Kind);
            command.Parameters.AddWithValue("@region", entry.Region);
            command.Parameters.AddWithValue("@name", entry.Name);
            command.Parameters.AddWithValue("@title", entry.Title);
            command.Parameters.AddWithValue("@source_url", entry.SourceUrl);
            command.Parameters.AddWithValue("@content_text", entry.ContentText);
            command.Parameters.AddWithValue("@content_html", entry.ContentHtml);
            command.Parameters.AddWithValue("@synced_at", entry.SyncedAt);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        return new SqliteConnection($"Data Source={_databasePath}");
    }
}
