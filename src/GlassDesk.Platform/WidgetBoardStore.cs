using System.Text.Json;
using GlassDesk.Core;

namespace GlassDesk.Platform;

/// <summary>Independent, bounded widget configuration in an explicitly selected preview profile.</summary>
public sealed class WidgetBoardStore : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _loadNotice;
    private volatile bool _canSave = true;
    public bool CanSave => _canSave;
    public string DirectoryPath { get; }
    public string SettingsPath => Path.Combine(DirectoryPath, "widgets.json");

    public WidgetBoardStore(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        DirectoryPath = Path.GetFullPath(profileDirectory);
    }

    public string? TakeLoadNotice() => Interlocked.Exchange(ref _loadNotice, null);

    public async Task<WidgetBoardState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _loadNotice = null;
            _canSave = true;
            try { return Parse(await ReadBoundedAsync(cancellationToken).ConfigureAwait(false)); }
            catch (Exception error) when (error is JsonException or InvalidDataException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string backup = Path.Combine(DirectoryPath, $"widgets.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
                try
                {
                    File.Move(SettingsPath, backup);
                    _loadNotice = "小组件配置无法读取，已保留备份并恢复默认组件。";
                    return await DefaultsAsync(true, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
                {
                    // Never overwrite a bad original if its backup could not be preserved.
                    _canSave = false;
                    _loadNotice = "原组件配置未能备份，当前为临时展示；本次不保存，请恢复访问后重启。";
                    return WidgetBoardData.CreateDefault();
                }
            }
            catch (FileNotFoundException) { return await DefaultsAsync(false, cancellationToken).ConfigureAwait(false); }
            catch (DirectoryNotFoundException) { return await DefaultsAsync(false, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _canSave = false;
                _loadNotice = "原组件配置暂时无法读取，当前为临时展示；本次不保存，请恢复访问后重启。";
                return WidgetBoardData.CreateDefault();
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(WidgetBoardState state, CancellationToken cancellationToken = default)
    {
        // Clone before awaiting; a caller-owned JsonDocument may be disposed during the write.
        var copy = WidgetBoardData.ValidateAndClone(state);
        var bytes = Serialize(copy);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_canSave) throw new IOException("原组件配置尚未成功读取，为保护原文件已阻止保存。请恢复访问后重新加载。");
            await SaveBytesAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<WidgetBoardState> DefaultsAsync(bool corruptionBackedUp, CancellationToken cancellationToken)
    {
        var defaults = WidgetBoardData.CreateDefault();
        try { await SaveBytesAsync(Serialize(defaults), cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _canSave = false;
            _loadNotice = corruptionBackedUp ? "已保留损坏配置备份，默认组件暂未保存。" : "已使用默认组件，配置暂未保存。";
        }
        return defaults;
    }

    private async Task<byte[]> ReadBoundedAsync(CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > WidgetBoardData.MaximumFileBytes) throw new InvalidDataException("Widget board exceeds 256 KiB.");
        using var output = new MemoryStream((int)stream.Length);
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > WidgetBoardData.MaximumFileBytes) throw new InvalidDataException("Widget board exceeds 256 KiB.");
            output.Write(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    private async Task SaveBytesAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length > WidgetBoardData.MaximumFileBytes) throw new InvalidDataException("Widget board exceeds 256 KiB.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DirectoryPath);
        string? temporary = Path.Combine(DirectoryPath, $".widgets-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // The temp file lives on the same volume. Replacement exposes a whole old or whole new JSON file.
            if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
            else File.Move(temporary, SettingsPath);
            temporary = null;
        }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static byte[] Serialize(WidgetBoardState state)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject(); writer.WriteNumber("version", state.Version);
            writer.WriteStartArray("instances");
            foreach (var instance in state.Instances)
            {
                writer.WriteStartObject(); writer.WriteString("instanceId", instance.InstanceId);
                writer.WriteString("providerId", instance.ProviderId);
                writer.WriteString("size", instance.Size == WidgetCardSize.Small ? "small" : "medium");
                writer.WriteNumber("order", instance.Order); writer.WritePropertyName("settings");
                // Preserve the already bounded UTF-8 JSON spelling. Re-encoding non-ASCII strings with
                // the default encoder could expand a valid 4 KiB settings value beyond its own load limit.
                writer.WriteRawValue(instance.Settings.GetRawText()); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        var bytes = output.ToArray();
        if (bytes.Length > WidgetBoardData.MaximumFileBytes) throw new InvalidDataException("Widget board exceeds 256 KiB.");
        return bytes;
    }

    private static WidgetBoardState Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = WidgetBoardData.MaximumSettingsDepth + 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid widget board root.");
        var versionValue = Property(root, "version");
        if (versionValue.ValueKind != JsonValueKind.Number || !versionValue.TryGetInt32(out int version)
            || version != WidgetBoardData.Version) throw new InvalidDataException("Unsupported widget board version.");
        var array = Property(root, "instances");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > WidgetBoardData.MaximumInstances)
            throw new InvalidDataException("Invalid widget instances.");
        var instances = new List<WidgetInstance>(array.GetArrayLength());
        foreach (var value in array.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid widget instance.");
            string id = StringProperty(value, "instanceId"), provider = StringProperty(value, "providerId");
            var size = StringProperty(value, "size") switch
            { "small" => WidgetCardSize.Small, "medium" => WidgetCardSize.Medium, _ => throw new InvalidDataException("Invalid widget size.") };
            var orderValue = Property(value, "order");
            if (orderValue.ValueKind != JsonValueKind.Number || !orderValue.TryGetInt32(out int order)) throw new InvalidDataException("Invalid widget order.");
            instances.Add(new(id, provider, size, order, Property(value, "settings")));
        }
        return WidgetBoardData.ValidateAndClone(new(version, instances));
    }

    private static JsonElement Property(JsonElement value, string name)
    {
        JsonElement result = default; int matches = 0;
        foreach (var property in value.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { result = property.Value; matches++; }
        if (matches != 1) throw new InvalidDataException("Missing or duplicate widget property.");
        return result;
    }
    private static string StringProperty(JsonElement value, string name)
    {
        var property = Property(value, name);
        return property.ValueKind == JsonValueKind.String ? property.GetString()! : throw new InvalidDataException("Invalid widget string.");
    }

    public void Dispose() => _gate.Dispose();
}
