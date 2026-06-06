using Dapper;
using LocalResourceExplorer.Services;

namespace LocalResourceExplorer.Repositories;

public sealed class AppSettingsRepository
{
    private readonly DatabaseService databaseService;

    public AppSettingsRepository(DatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public async Task<string?> GetAsync(string key)
    {
        const string sql = """
            SELECT value
            FROM app_settings
            WHERE key = @Key;
            """;

        await using var connection = databaseService.GetConnection();
        return await connection.QuerySingleOrDefaultAsync<string?>(sql, new { Key = key });
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync()
    {
        const string sql = """
            SELECT key, value
            FROM app_settings;
            """;

        await using var connection = databaseService.GetConnection();
        var rows = await connection.QueryAsync<AppSettingRow>(sql);
        return rows
            .Where(row => row.Value is not null)
            .ToDictionary(row => row.Key, row => row.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetAsync(string key, string? value)
    {
        const string sql = """
            INSERT INTO app_settings (key, value, updated_at)
            VALUES (@Key, @Value, @UpdatedAt)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = excluded.updated_at;
            """;

        await using var connection = databaseService.GetConnection();
        await connection.ExecuteAsync(sql, new
        {
            Key = key,
            Value = value,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task RemoveAsync(string key)
    {
        const string sql = """
            DELETE FROM app_settings
            WHERE key = @Key;
            """;

        await using var connection = databaseService.GetConnection();
        await connection.ExecuteAsync(sql, new { Key = key });
    }

    private sealed class AppSettingRow
    {
        public string Key { get; set; } = string.Empty;

        public string? Value { get; set; }
    }
}
