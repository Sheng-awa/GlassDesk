using System.Runtime.InteropServices;

// Test-only generated PCM, kept in memory. Never reads a microphone or writes
// audio to disk. Separate sibling processes can render an exclusion test tone.
sealed class ToneOutput : IDisposable
{
    private nint _device, _header;
    private GCHandle _samplesPin;
    private short[]? _samples;
    public ToneOutput(int frequency, int durationMs)
    {
        var format = new WaveFormat { Tag = 1, Channels = 2, SampleRate = 48000, AverageBytes = 192000, Align = 4, Bits = 16 };
        Check(waveOutOpen(out _device, uint.MaxValue, ref format, 0, 0, 0));
        try
        {
            _samples = new short[durationMs * 48 * 2];
            for (var frame = 0; frame < _samples.Length / 2; frame++)
                _samples[frame * 2] = _samples[frame * 2 + 1] = (short)(3276 * Math.Sin(2 * Math.PI * frequency * frame / 48000));
            _samplesPin = GCHandle.Alloc(_samples, GCHandleType.Pinned);
            _header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
            Marshal.StructureToPtr(new WaveHeader { Data = _samplesPin.AddrOfPinnedObject(), BufferLength = (uint)(_samples.Length * 2) }, _header, false);
            Check(waveOutPrepareHeader(_device, _header, (uint)Marshal.SizeOf<WaveHeader>()));
            Check(waveOutWrite(_device, _header, (uint)Marshal.SizeOf<WaveHeader>()));
        }
        catch { Dispose(); throw; }
    }
    private static void Check(uint result) { if (result != 0) throw new InvalidOperationException($"waveOut test renderer error {result}."); }
    public void Dispose()
    {
        if (_device != 0)
        {
            waveOutReset(_device);
            if (_header != 0) waveOutUnprepareHeader(_device, _header, (uint)Marshal.SizeOf<WaveHeader>());
            waveOutClose(_device); _device = 0;
        }
        if (_header != 0) { Marshal.FreeHGlobal(_header); _header = 0; }
        if (_samplesPin.IsAllocated) _samplesPin.Free();
        if (_samples is not null) { Array.Clear(_samples); _samples = null; }
    }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat
    {
        public ushort Tag, Channels;
        public uint SampleRate, AverageBytes;
        public ushort Align, Bits, Extra;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader
    {
        public nint Data;
        public uint BufferLength, BytesRecorded;
        public nuint User;
        public uint Flags, Loops;
        public nint Next;
        public nuint Reserved;
    }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out nint device, uint id, ref WaveFormat format, nuint callback, nuint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(nint device);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(nint device);
}
