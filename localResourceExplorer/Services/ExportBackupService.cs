using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalResourceExplorer.Models;

namespace LocalResourceExplorer.Services;

public sealed class ExportBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly DatabaseService databaseService;

    public ExportBackupService(DatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public async Task ExportResourcesToCsvAsync(IEnumerable<ResourceItem> resources, string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await writer.WriteLineAsync(
            "id,title,original_name,path,extension,size_bytes,modified_at,imported_at,duration_ms,note,is_favorite,is_missing,last_opened_at,created_at,updated_at");

        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = string.Join(
                ',',
                Csv(resource.Id),
                Csv(resource.Title),
                Csv(resource.OriginalName),
                Csv(resource.Path),
                Csv(resource.Extension),
                Csv(resource.SizeBytes),
                Csv(resource.ModifiedAt),
                Csv(resource.ImportedAt),
                Csv(resource.DurationMs),
                Csv(resource.Note),
                Csv(resource.IsFavorite),
                Csv(resource.IsMissing),
                Csv(resource.LastOpenedAt),
                Csv(resource.CreatedAt),
                Csv(resource.UpdatedAt));

            await writer.WriteLineAsync(row);
        }
    }

    public async Task ExportResourcesToJsonAsync(IEnumerable<ResourceItem> resources, string filePath, CancellationToken cancellationToken = default)
    {
        var exportPayload = resources.Select(resource => new
        {
            id = resource.Id,
            title = resource.Title,
            original_name = resource.OriginalName,
            path = resource.Path,
            extension = resource.Extension,
            size_bytes = resource.SizeBytes,
            modified_at = resource.ModifiedAt,
            imported_at = resource.ImportedAt,
            duration_ms = resource.DurationMs,
            note = resource.Note,
            is_favorite = resource.IsFavorite,
            is_missing = resource.IsMissing,
            last_opened_at = resource.LastOpenedAt,
            created_at = resource.CreatedAt,
            updated_at = resource.UpdatedAt
        }).ToArray();

        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, exportPayload, JsonOptions, cancellationToken);
    }

    public async Task BackupDatabaseAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await databaseService.InitializeAsync(cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath) ?? Environment.CurrentDirectory);

            await using var source = new FileStream(databaseService.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var destination = new FileStream(backupFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.DatabaseError(ex, "Database backup failed", backupFilePath);
            throw;
        }
    }

    public async Task RestoreDatabaseAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(backupFilePath))
            {
                throw new FileNotFoundException("备份文件不存在。", backupFilePath);
            }

            Directory.CreateDirectory(databaseService.DatabaseDirectory);

            await using var source = new FileStream(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var destination = new FileStream(databaseService.DatabasePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, cancellationToken);

            await databaseService.InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.DatabaseError(ex, "Database restore failed", backupFilePath);
            throw;
        }
    }

    private static string Csv(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
        {
            text = '"' + text.Replace("\"", "\"\"") + '"';
        }

        return text;
    }
}
