namespace GlassDesk.Platform;

/// <summary>Reusable Hann-windowed radix-2 FFT. Stereo energy is combined after
/// FFT so anti-phase stereo cannot cancel. Output is normalized -60..0 dBFS.</summary>
public sealed class AudioSpectrumAnalyzer
{
    public const int WindowFrames = 2048;
    public const int BandCount = 8;
    private static readonly double[] Edges = [20, 60, 150, 400, 1000, 2500, 6000, 12000, 24000];
    private readonly double[] _window = new double[WindowFrames];
    private readonly double[] _leftReal = new double[WindowFrames], _leftImag = new double[WindowFrames];
    private readonly double[] _rightReal = new double[WindowFrames], _rightImag = new double[WindowFrames];
    private readonly double[] _power = new double[BandCount];
    private readonly double _scale;

    public AudioSpectrumAnalyzer()
    {
        double sum = 0;
        for (var index = 0; index < WindowFrames; index++) { _window[index] = .5 * (1 - Math.Cos(2 * Math.PI * index / (WindowFrames - 1))); sum += _window[index]; }
        _scale = 2 / sum;
    }

    public float Analyze(ReadOnlySpan<float> stereoSamples, int sampleRate, Span<float> bands)
    {
        if (sampleRate <= 0 || bands.Length != BandCount || stereoSamples.Length % 2 != 0) throw new ArgumentException("Expected stereo samples, a positive sample rate and eight bands.");
        bands.Clear();
        var frames = Math.Min(stereoSamples.Length / 2, WindowFrames);
        if (frames == 0) return 0;
        stereoSamples = stereoSamples[^ (frames * 2)..];
        Array.Clear(_leftReal); Array.Clear(_leftImag); Array.Clear(_rightReal); Array.Clear(_rightImag); Array.Clear(_power);
        double energy = 0;
        for (var index = 0; index < frames; index++)
        {
            var left = Clean(stereoSamples[index * 2]); var right = Clean(stereoSamples[index * 2 + 1]);
            energy += (left * left + right * right) / 2;
            var offset = WindowFrames - frames + index;
            _leftReal[offset] = left * _window[offset]; _rightReal[offset] = right * _window[offset];
        }
        var rms = (float)Math.Sqrt(energy / frames);
        // Silence must be exact zero; no synthetic fallback or decay floor.
        if (rms < 0.000001f) return 0;
        Transform(_leftReal, _leftImag); Transform(_rightReal, _rightImag);
        for (var bin = 1; bin < WindowFrames / 2; bin++)
        {
            var frequency = (double)bin * sampleRate / WindowFrames;
            var band = 0;
            while (band < BandCount && frequency >= Edges[band + 1]) band++;
            if (band == BandCount || frequency < Edges[0]) continue;
            _power[band] += (_leftReal[bin] * _leftReal[bin] + _leftImag[bin] * _leftImag[bin]
                + _rightReal[bin] * _rightReal[bin] + _rightImag[bin] * _rightImag[bin]) / 2;
        }
        for (var band = 0; band < BandCount; band++)
        {
            var amplitude = Math.Sqrt(_power[band]) * _scale;
            bands[band] = amplitude <= .001 ? 0 : (float)Math.Clamp((20 * Math.Log10(amplitude) + 60) / 60, 0, 1);
        }
        return rms;
    }

    private static double Clean(float value) => float.IsFinite(value) ? Math.Clamp((double)value, -1, 1) : 0;
    private static void Transform(double[] real, double[] imaginary)
    {
        for (int index = 1, reverse = 0; index < WindowFrames; index++)
        {
            var bit = WindowFrames >> 1;
            while ((reverse & bit) != 0) { reverse ^= bit; bit >>= 1; }
            reverse ^= bit;
            if (index < reverse) { (real[index], real[reverse]) = (real[reverse], real[index]); (imaginary[index], imaginary[reverse]) = (imaginary[reverse], imaginary[index]); }
        }
        for (var size = 2; size <= WindowFrames; size <<= 1)
        {
            var angle = -2 * Math.PI / size;
            var stepReal = Math.Cos(angle); var stepImaginary = Math.Sin(angle);
            for (var start = 0; start < WindowFrames; start += size)
            {
                double twiddleReal = 1, twiddleImaginary = 0;
                for (var offset = 0; offset < size / 2; offset++)
                {
                    var even = start + offset; var odd = even + size / 2;
                    var valueReal = real[odd] * twiddleReal - imaginary[odd] * twiddleImaginary;
                    var valueImaginary = real[odd] * twiddleImaginary + imaginary[odd] * twiddleReal;
                    real[odd] = real[even] - valueReal; imaginary[odd] = imaginary[even] - valueImaginary;
                    real[even] += valueReal; imaginary[even] += valueImaginary;
                    var nextReal = twiddleReal * stepReal - twiddleImaginary * stepImaginary;
                    twiddleImaginary = twiddleReal * stepImaginary + twiddleImaginary * stepReal; twiddleReal = nextReal;
                }
            }
        }
    }
}
