using System.Buffers.Binary;

namespace KotobaSUB.Core.Audio;

public enum PcmSampleEncoding { Signed16, Signed24, Signed32, Float32 }

public sealed class AudioSampleConverter
{
    private readonly int sourceRate;
    private readonly int channels;
    private readonly PcmSampleEncoding encoding;
    private readonly double outputStep;
    private bool hasPrevious;
    private float previous;
    private long sourceIndex;
    private double nextOutput;
    public AudioSampleConverter(int sourceRate, int channels, PcmSampleEncoding encoding)
    {
        if (sourceRate < 8000 || sourceRate > 384000 || channels < 1 || channels > 32) throw new ArgumentOutOfRangeException();
        this.sourceRate = sourceRate; this.channels = channels; this.encoding = encoding; outputStep = sourceRate / 16000d;
    }
    public float[] Convert(ReadOnlySpan<byte> bytes)
    {
        int sampleBytes = encoding switch { PcmSampleEncoding.Signed16 => 2, PcmSampleEncoding.Signed24 => 3, _ => 4 };
        int frameBytes = checked(sampleBytes * channels);
        if (bytes.Length % frameBytes != 0) throw new InvalidDataException("Audio buffer is not frame-aligned");
        int frames = bytes.Length / frameBytes;
        if (frames == 0) return [];
        var mono = new float[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            double sum = 0; int offset = frame * frameBytes;
            for (int channel = 0; channel < channels; channel++) sum += Read(bytes.Slice(offset + channel * sampleBytes, sampleBytes));
            mono[frame] = (float)Math.Clamp(sum / channels, -1, 1);
        }
        var output = new List<float>((int)Math.Ceiling(frames / outputStep) + 1);
        foreach (float current in mono)
        {
            if (!hasPrevious)
            {
                previous = current; hasPrevious = true; sourceIndex = 0; nextOutput = outputStep; output.Add(current); continue;
            }
            long currentIndex = sourceIndex + 1;
            while (nextOutput <= currentIndex)
            {
                double amount = nextOutput - sourceIndex;
                output.Add((float)(previous + (current - previous) * amount));
                nextOutput += outputStep;
            }
            previous = current; sourceIndex = currentIndex;
        }
        return output.ToArray();
    }
    public void Reset() { hasPrevious = false; previous = 0; sourceIndex = 0; nextOutput = 0; }
    private float Read(ReadOnlySpan<byte> value) => encoding switch
    {
        PcmSampleEncoding.Signed16 => BinaryPrimitives.ReadInt16LittleEndian(value) / 32768f,
        PcmSampleEncoding.Signed24 => ((value[0] | value[1] << 8 | value[2] << 16) << 8 >> 8) / 8388608f,
        PcmSampleEncoding.Signed32 => BinaryPrimitives.ReadInt32LittleEndian(value) / 2147483648f,
        PcmSampleEncoding.Float32 => float.IsFinite(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(value))) ? Math.Clamp(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(value)), -1, 1) : 0,
        _ => 0
    };
}
