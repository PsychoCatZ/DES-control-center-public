using System.Globalization;
using Dapper;
using DesControlCenter.Core.Abstractions;
using DesControlCenter.Core.Enums;
using DesControlCenter.Core.Models;
using Microsoft.Data.Sqlite;

namespace DesControlCenter.Infrastructure.Persistence;

/// <summary>Local, durable event log at %AppData%\DesControlCenter\events.db.</summary>
public sealed class SqliteEventStore : IEventStore
{
    private readonly string _connectionString;

    public SqliteEventStore(string? dbPath = null)
    {
        var path = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesControlCenter", "events.db");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS Events (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                ResourceKind TEXT NOT NULL,
                ResourceId TEXT NOT NULL,
                ResourceName TEXT NOT NULL,
                ProjectId TEXT NULL,
                ProjectName TEXT NULL,
                EventKind TEXT NOT NULL,
                OldState TEXT NULL,
                NewState TEXT NULL,
                Message TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Events_TimestampUtc ON Events(TimestampUtc);
            """);
    }

    public async Task AddAsync(EventLogEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO Events (TimestampUtc, ResourceKind, ResourceId, ResourceName, ProjectId, ProjectName, EventKind, OldState, NewState, Message)
            VALUES (@TimestampUtc, @ResourceKind, @ResourceId, @ResourceName, @ProjectId, @ProjectName, @EventKind, @OldState, @NewState, @Message)
            """,
            new
            {
                TimestampUtc = entry.TimestampUtc.UtcDateTime.ToString("O"),
                ResourceKind = entry.ResourceKind.ToString(),
                ResourceId = entry.ResourceId.ToString(),
                entry.ResourceName,
                ProjectId = entry.ProjectId?.ToString(),
                entry.ProjectName,
                EventKind = entry.EventKind.ToString(),
                entry.OldState,
                entry.NewState,
                entry.Message
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<EventLogEntry>> QueryAsync(EventLogFilter filter, CancellationToken cancellationToken = default)
    {
        var where = new List<string>();
        var parameters = new DynamicParameters();

        if (filter.FromUtc is { } from)
        {
            where.Add("TimestampUtc >= @From");
            parameters.Add("From", from.UtcDateTime.ToString("O"));
        }

        if (filter.ToUtc is { } to)
        {
            where.Add("TimestampUtc <= @To");
            parameters.Add("To", to.UtcDateTime.ToString("O"));
        }

        if (filter.ProjectId is { } projectId)
        {
            where.Add("ProjectId = @ProjectId");
            parameters.Add("ProjectId", projectId.ToString());
        }

        if (filter.ResourceId is { } resourceId)
        {
            where.Add("ResourceId = @ResourceId");
            parameters.Add("ResourceId", resourceId.ToString());
        }

        if (filter.ResourceKind is { } resourceKind)
        {
            where.Add("ResourceKind = @ResourceKind");
            parameters.Add("ResourceKind", resourceKind.ToString());
        }

        if (filter.EventKind is { } eventKind)
        {
            where.Add("EventKind = @EventKind");
            parameters.Add("EventKind", eventKind.ToString());
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
        parameters.Add("Limit", filter.Limit);

        var sql = $"SELECT * FROM Events {whereClause} ORDER BY TimestampUtc DESC LIMIT @Limit";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<EventRow>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        return rows.Select(MapRow).ToList();
    }

    private static EventLogEntry MapRow(EventRow row) => new()
    {
        Id = row.Id,
        TimestampUtc = DateTimeOffset.Parse(row.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        ResourceKind = Enum.Parse<ResourceKind>(row.ResourceKind),
        ResourceId = Guid.Parse(row.ResourceId),
        ResourceName = row.ResourceName,
        ProjectId = row.ProjectId is null ? null : Guid.Parse(row.ProjectId),
        ProjectName = row.ProjectName,
        EventKind = Enum.Parse<EventKind>(row.EventKind),
        OldState = row.OldState,
        NewState = row.NewState,
        Message = row.Message
    };

    private sealed class EventRow
    {
        public long Id { get; set; }
        public string TimestampUtc { get; set; } = string.Empty;
        public string ResourceKind { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string ResourceName { get; set; } = string.Empty;
        public string? ProjectId { get; set; }
        public string? ProjectName { get; set; }
        public string EventKind { get; set; } = string.Empty;
        public string? OldState { get; set; }
        public string? NewState { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
