using System.Runtime.InteropServices;

namespace GlassDesk.Platform;

public sealed record HomeDirectoryEntry(string Name, string Path, bool IsFolder);
public sealed record HomeDirectorySnapshot(string Name, string Path, IReadOnlyList<HomeDirectoryEntry> Entries);

/// <summary>Metadata only. A slow/re-directed directory never runs on the UI dispatcher.</summary>
public sealed class HomeDirectoryService
{
    readonly object _gate = new();
    readonly Dictionary<int, Task<HomeDirectorySnapshot>> _reads = new();
    readonly Func<int, HomeDirectorySnapshot> _read;
    readonly TimeSpan _timeout;
    public HomeDirectoryService(Func<int, HomeDirectorySnapshot>? read = null, TimeSpan? timeout = null)
    {
        _read = read ?? ReadDirectory;
        _timeout = timeout ?? TimeSpan.FromSeconds(4);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(4)) throw new ArgumentOutOfRangeException(nameof(timeout));
    }
    public async Task<HomeDirectorySnapshot> ReadAsync(int index, CancellationToken cancellation)
    {
        if (index is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(index));
        cancellation.ThrowIfCancellationRequested();
        Task<HomeDirectorySnapshot> work;
        lock (_gate)
        {
            if (!_reads.TryGetValue(index, out work!) || work.IsCompleted)
            {
                // At most one OS read per directory (three total). Cancellation/timeout
                // detaches a caller, not the underlying synchronous OS call. Other
                // directories stay independent; repeated clicks reuse a hung read.
                work = Task.Run(() => _read(index));
                _reads[index] = work;
                _ = work.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        return await work.WaitAsync(_timeout, cancellation).ConfigureAwait(false);
    }
    static HomeDirectorySnapshot ReadDirectory(int index)
    {
        string name = index == 0 ? "桌面" : index == 1 ? "文档" : "下载";
        string path = index == 0 ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : index == 1 ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Downloads();
        if (string.IsNullOrWhiteSpace(path)) throw new DirectoryNotFoundException("系统文件夹不可用");
        var entries = new List<HomeDirectoryEntry>();
        // Bound metadata enumeration. Do not request thumbnails or hydrate cloud files.
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos().Take(128))
            entries.Add(new(entry.Name, entry.FullName, entry is DirectoryInfo));
        var preview = entries.OrderByDescending(e => e.IsFolder).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).Take(3).ToArray();
        return new HomeDirectorySnapshot(name, path, preview);
    }
    static string Downloads()
    {
        Guid folder = new("374DE290-123F-4565-9164-39C4925E467B");
        int hr = SHGetKnownFolderPath(in folder, 0, 0, out var pointer);
        try { Marshal.ThrowExceptionForHR(hr); return Marshal.PtrToStringUni(pointer) ?? throw new DirectoryNotFoundException(); }
        finally { if (pointer != 0) Marshal.FreeCoTaskMem(pointer); }
    }
    [DllImport("shell32.dll", ExactSpelling = true)]
    static extern int SHGetKnownFolderPath(in Guid folder, uint flags, nint token, out nint path);
}

