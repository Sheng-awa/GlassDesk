using System.Text.Json;
using GlassDesk.Core;

namespace GlassDesk.Platform;

/// <summary>Versioned preferences only. Song content and account information never enter this store.</summary>
public sealed class PreferenceStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private string? _loadNotice;
    public string DirectoryPath { get; }
    public string SettingsPath => Path.Combine(DirectoryPath, "settings.json");

    // Explicit directory is useful for isolated tests and portable diagnostic fixtures.
    public PreferenceStore(string? directoryPath = null) => DirectoryPath = directoryPath ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassDesk");

    public WidgetPreferences Load()
    {
        _loadNotice = null;
        if (!File.Exists(SettingsPath)) return new();
        try
        {
            var info = new FileInfo(SettingsPath);
            if (info.Length > 64 * 1024) throw new InvalidDataException("Preferences exceed the size limit.");
            var preferences = JsonSerializer.Deserialize<WidgetPreferences>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? throw new InvalidDataException("Preferences are empty.");
            if (preferences.Version is not (1 or 2 or 3 or 4)) throw new InvalidDataException("Unsupported preferences version.");
            return Normalize(preferences);
        }
        catch (Exception error) when (error is JsonException or IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            // Preserve the evidence once, then restore usable defaults. Do not silently delete user data.
            var backedUp = false;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.Move(SettingsPath, Path.Combine(DirectoryPath,
                    $"settings.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"));
                backedUp = true;
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException) { }
            _loadNotice = backedUp ? "配置文件无法读取，已保留备份并恢复默认设置。" : "配置文件无法读取，已使用默认设置；原文件仍保留。";
            return new();
        }
    }

    public string? TakeLoadNotice() => Interlocked.Exchange(ref _loadNotice, null);

    public async Task SaveAsync(WidgetPreferences preferences, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            temporary = Path.Combine(DirectoryPath, $".settings-{Guid.NewGuid():N}.tmp");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Normalize(preferences), JsonOptions);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
            else File.Move(temporary, SettingsPath);
            temporary = null;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            _writeGate.Release();
        }
    }

    public static WidgetPreferences Normalize(WidgetPreferences preferences)
    {
        var normalized = preferences.Validated();
        return normalized with
        {
            Blur = float.IsFinite(preferences.Blur) ? Math.Clamp(preferences.Blur, 0, 40) : 20,
            TintOpacity = float.IsFinite(preferences.TintOpacity) ? Math.Clamp(preferences.TintOpacity, .10f, .90f) : .24f,
            HotkeyModifiers = preferences.HotkeyModifiers & 0xf,
            HotkeyKey = preferences.HotkeyKey <= 0xff ? preferences.HotkeyKey : 0,
            MonitorId = preferences.MonitorId is { Length: > 512 } ? null : preferences.MonitorId
        };
    }

    public void Dispose() => _writeGate.Dispose();
}
