using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GlassDesk.Core;
using Microsoft.Win32.SafeHandles;

namespace GlassDesk.Platform;

public readonly record struct AudioProcessIdentity(uint ProcessId, ulong CreationFileTime);
public sealed record AudioProcessResolution(AudioProcessIdentity? Identity, string? Reason = null);
public interface INetEaseProcessResolver
{
    AudioProcessResolution Resolve(MediaSnapshot media);
}

/// <summary>
/// GSMTC supplies cloudmusic.exe's App ID, not its PID. Bind only when there is
/// exactly one verified NetEase process tree. Never guess from foreground state,
/// song titles, the default audio endpoint, or an arbitrary active audio session.
/// </summary>
public sealed class NetEaseProcessResolver : INetEaseProcessResolver
{
    public AudioProcessResolution Resolve(MediaSnapshot media)
    {
        if (!media.Connected || string.IsNullOrEmpty(media.SessionToken)) return new(null, "没有网易云媒体会话。");
        try
        {
            var processes = Snapshot();
            var netEaseIds = processes.Where(process => string.Equals(process.ExecutableName, "cloudmusic.exe", StringComparison.OrdinalIgnoreCase))
                .Select(process => process.ProcessId).ToHashSet();
            return SelectUniqueTree(processes, id => ReadIdentity(id, netEaseIds.Contains(id)));
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException or IOException)
        { return new(null, "无法可靠读取网易云进程身份。"); }
    }

    // Exposed pure selection policy permits ambiguity/PID-reuse checks without
    // creating real music processes or controlling any desktop application.
    public static AudioProcessResolution SelectUniqueTree(IReadOnlyList<AudioProcessEntry> processes,
        Func<uint, AudioProcessIdentity?> identityReader)
    {
        var byId = processes.ToDictionary(process => process.ProcessId);
        var candidates = processes.Where(process => string.Equals(process.ExecutableName, "cloudmusic.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0) return new(null, "网易云进程未运行。");
        var identities = new Dictionary<uint, AudioProcessIdentity>();
        foreach (var candidate in candidates)
        {
            var verified = identityReader(candidate.ProcessId);
            if (verified is not { ProcessId: > 0, CreationFileTime: > 0 } || verified.Value.ProcessId != candidate.ProcessId)
                return new(null, "网易云可执行文件或进程身份无法验证。");
            identities[candidate.ProcessId] = verified.Value;
        }
        var roots = new List<AudioProcessEntry>();
        foreach (var candidate in candidates)
        {
            var seen = new HashSet<uint> { candidate.ProcessId };
            var parent = candidate.ParentProcessId;
            var childOfNetEase = false;
            var ancestorPath = new List<uint>();
            while (parent != 0 && byId.TryGetValue(parent, out var ancestor))
            {
                if (!seen.Add(parent)) return new(null, "进程树无法可靠识别。");
                if (string.Equals(ancestor.ExecutableName, "cloudmusic.exe", StringComparison.OrdinalIgnoreCase))
                {
                    // A parent PID can be reused after the real parent exits. A
                    // newer cloudmusic process is not this older process's parent.
                    ancestorPath.Add(ancestor.ProcessId);
                    var descendantCreation = identities[candidate.ProcessId].CreationFileTime;
                    childOfNetEase = true;
                    foreach (var ancestorId in ancestorPath)
                    {
                        AudioProcessIdentity? parentIdentity = identities.TryGetValue(ancestorId, out var known) ? known : identityReader(ancestorId);
                        if (parentIdentity is not { ProcessId: > 0, CreationFileTime: > 0 } || parentIdentity.Value.ProcessId != ancestorId)
                            return new(null, "进程树祖先身份无法验证。");
                        if (parentIdentity.Value.CreationFileTime > descendantCreation) { childOfNetEase = false; break; }
                        descendantCreation = parentIdentity.Value.CreationFileTime;
                    }
                    break;
                }
                ancestorPath.Add(parent);
                parent = ancestor.ParentProcessId;
            }
            if (!childOfNetEase) roots.Add(candidate);
        }
        if (roots.Count != 1) return new(null, "检测到多个网易云实例，无法将媒体会话唯一映射到进程。");
        return new(identities[roots[0].ProcessId]);
    }

    private static AudioProcessIdentity? ReadIdentity(uint id, bool requireNetEase)
    {
        using var process = OpenProcess(0x1000 | 0x00100000, false, id); // QUERY_LIMITED_INFORMATION | SYNCHRONIZE
        if (process.IsInvalid || WaitForSingleObject(process, 0) != 258) return null;
        var path = new StringBuilder(32768);
        uint length = (uint)path.Capacity;
        if (!QueryFullProcessImageName(process, 0, path, ref length)) return null;
        var executable = path.ToString();
        if (requireNetEase)
        {
            if (!string.Equals(Path.GetFileName(executable), "cloudmusic.exe", StringComparison.OrdinalIgnoreCase)) return null;
            var version = FileVersionInfo.GetVersionInfo(executable);
            if (!string.Equals(version.CompanyName?.Trim(), "NetEase", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(version.OriginalFilename?.Trim(), "cloudmusic.exe", StringComparison.OrdinalIgnoreCase)) return null;
        }
        if (!GetProcessTimes(process, out var creation, out _, out _, out _)) return null;
        return new(id, creation);
    }

    private static List<AudioProcessEntry> Snapshot()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Executable = "" };
        var result = new List<AudioProcessEntry>();
        if (!Process32First(snapshot, ref entry)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        do { result.Add(new(entry.ProcessId, entry.ParentProcessId, entry.Executable)); }
        while (Process32Next(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); // ERROR_NO_MORE_FILES
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public nuint DefaultHeap;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriorityClass;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeFileHandle process, uint timeout);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeFileHandle process, uint flags, StringBuilder name, ref uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeFileHandle process, out ulong creation, out ulong exit, out ulong kernel, out ulong user);
}

public readonly record struct AudioProcessEntry(uint ProcessId, uint ParentProcessId, string ExecutableName);
