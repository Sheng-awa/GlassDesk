using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Media.Control;

// P0 diagnostic: only an explicitly selected source may receive commands.
// Reports contain capability and content-presence facts, never song history.
static string? Option(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var source = Option(args, "--source");
var command = Option(args, "--command");
var output = Option(args, "--out");
var observations = new List<object>();
var exitCode = 0;
try
{
    // Keep the broker/session on its creating COM thread. GSMTC commands are
    // not guaranteed agile; a console await continuation can yield RPC_E_WRONG_THREAD.
    var manager = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
    var sessions = manager.GetSessions();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Sources = sessions.Select(s => s.SourceAppUserModelId).ToArray(),
        CurrentSource = manager.GetCurrentSession()?.SourceAppUserModelId
    }));
    if (source is not null)
    {
        var matches = sessions.Where(s => string.Equals(s.SourceAppUserModelId, source, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Selected source has {matches.Length} sessions; exactly one is required.");
        var session = matches[0];
        Sample Read()
        {
            var p = session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var c = playback.Controls;
            long coverBytes = 0;
            if (p.Thumbnail is not null)
            {
                using var stream = p.Thumbnail.OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
                coverBytes = checked((long)stream.Size);
            }
            var sample = new Sample(
                session.SourceAppUserModelId, !string.IsNullOrWhiteSpace(p.Title),
                !string.IsNullOrWhiteSpace(p.Artist), coverBytes,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Title + "\0" + p.Artist))),
                playback.PlaybackStatus.ToString(), playback.PlaybackRate,
                c.IsPreviousEnabled, c.IsPlayPauseToggleEnabled, c.IsPlayEnabled,
                c.IsPauseEnabled, c.IsNextEnabled, c.IsPlaybackPositionEnabled,
                timeline.StartTime.Ticks, timeline.EndTime.Ticks, timeline.Position.Ticks,
                timeline.MinSeekTime.Ticks, timeline.MaxSeekTime.Ticks, timeline.LastUpdatedTime);
            observations.Add(sample);
            Console.WriteLine(JsonSerializer.Serialize(sample));
            return sample;
        }
        var before = Read();
        if (command is not null)
        {
            var operation = command switch
            {
                "play" when before.CanPlay => session.TryPlayAsync(),
                "pause" when before.CanPause => session.TryPauseAsync(),
                "toggle" when before.CanToggle => session.TryTogglePlayPauseAsync(),
                "previous" when before.CanPrevious => session.TrySkipPreviousAsync(),
                "next" when before.CanNext => session.TrySkipNextAsync(),
                "seek" when before.CanSeek => session.TryChangePlaybackPositionAsync(Math.Clamp(
                    long.Parse(Option(args, "--ticks") ?? throw new ArgumentException("--ticks is required")),
                    before.MinSeekTicks, before.MaxSeekTicks)),
                _ => throw new InvalidOperationException("Command is unavailable or unsupported: " + command)
            };
            var accepted = operation.AsTask().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
            Sample after = before;
            bool verified = false;
            // Bounded real-media readback: false/timeout/unobserved changes fail P0.
            for (var i = 0; i < 12 && accepted; i++)
            {
                Thread.Sleep(250);
                after = Read();
                verified = command switch
                {
                    "play" => after.Status == "Playing",
                    "pause" => after.Status == "Paused",
                    "toggle" => after.Status != before.Status && after.Status is "Playing" or "Paused",
                    "next" or "previous" => after.ContentFingerprint != before.ContentFingerprint,
                    "seek" => Math.Abs(after.PositionTicks - long.Parse(Option(args, "--ticks")!)) < TimeSpan.FromSeconds(3).Ticks,
                    _ => false
                };
                if (verified) break;
            }
            var result = new { Command = command, Accepted = accepted, StateVerified = verified };
            observations.Add(result);
            Console.WriteLine(JsonSerializer.Serialize(result));
            if (!accepted || !verified) exitCode = 3;
        }
    }
    else if (command is not null)
        throw new ArgumentException("Commands require --source with an exact observed source ID.");
    observations.Insert(0, new { CapturedAt = DateTimeOffset.Now, Sources = sessions.Select(s => s.SourceAppUserModelId).ToArray(), SelectedSource = source });
}
catch (Exception ex)
{
    observations.Add(new { Error = ex.GetType().Name, ex.Message, ex.HResult });
    Console.Error.WriteLine(ex.GetType().Name + " (0x" + ex.HResult.ToString("X8") + "): " + ex.Message);
    exitCode = 2;
}
if (output is not null)
{
    var fullPath = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
}
return exitCode;

internal sealed record Sample(string Source, bool TitlePresent, bool ArtistPresent, long CoverBytes,
    string ContentFingerprint, string Status, double? Rate, bool CanPrevious, bool CanToggle,
    bool CanPlay, bool CanPause, bool CanNext, bool CanSeek, long StartTicks, long EndTicks,
    long PositionTicks, long MinSeekTicks, long MaxSeekTicks, DateTimeOffset TimelineUpdatedAt);
