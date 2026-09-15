namespace KotobaSUB.Core.Audio;

public sealed class SpeechActivityGate
{
    public const int SampleRate = 16000;
    private readonly float threshold;
    private readonly int minimumVoiced;
    private readonly int trailingSilence;
    private readonly int maximumWindow;
    private readonly int overlap;
    private readonly List<float> preRoll = new();
    private readonly List<float> active = new();
    private long samplesSeen;
    private bool hasTimeline;
    private long activeFirst;
    private int voicedSamples;
    private int silentSamples;

    public SpeechActivityGate(float rmsThreshold = .004f, double minimumVoicedSeconds = .24, double trailingSilenceSeconds = .6, double maximumWindowSeconds = 6, double overlapSeconds = .75)
    {
        if (rmsThreshold is <= 0 or > 1 || minimumVoicedSeconds <= 0 || trailingSilenceSeconds <= 0 || maximumWindowSeconds <= 1 || overlapSeconds < 0 || overlapSeconds >= maximumWindowSeconds) throw new ArgumentOutOfRangeException();
        threshold = rmsThreshold;
        minimumVoiced = Seconds(minimumVoicedSeconds);
        trailingSilence = Seconds(trailingSilenceSeconds);
        maximumWindow = Seconds(maximumWindowSeconds);
        overlap = Seconds(overlapSeconds);
    }

    public IReadOnlyList<SpeechWindow> Push(AudioBlock block)
    {
        if (block.SampleRate != SampleRate) throw new ArgumentException("Activity gate requires 16 kHz mono samples", nameof(block));
        if (block.Samples.Length == 0) return [];
        bool voiced = Rms(block.Samples) >= threshold;
        long declaredFirst = block.FirstSample < 0 ? samplesSeen : block.FirstSample;
        if (!hasTimeline) { samplesSeen = declaredFirst; hasTimeline = true; }
        else if (declaredFirst != samplesSeen) { preRoll.Clear(); ResetActive(); samplesSeen = declaredFirst; }
        long blockFirst = declaredFirst;
        samplesSeen = declaredFirst + block.Samples.Length;
        if (active.Count == 0)
        {
            AddBounded(preRoll, block.Samples, overlap);
            if (!voiced) return [];
            activeFirst = Math.Max(0, blockFirst + block.Samples.Length - preRoll.Count);
            active.AddRange(preRoll); preRoll.Clear();
            voicedSamples = block.Samples.Length; silentSamples = 0;
        }
        else
        {
            active.AddRange(block.Samples);
            if (voiced) { voicedSamples += block.Samples.Length; silentSamples = 0; }
            else silentSamples += block.Samples.Length;
        }
        if (silentSamples >= trailingSilence)
        {
            if (voicedSamples < minimumVoiced) { ResetActive(); return []; }
            var result = new SpeechWindow(active.ToArray(), activeFirst, true);
            ResetActive(); return [result];
        }
        if (active.Count >= maximumWindow)
        {
            var result = new SpeechWindow(active.Take(maximumWindow).ToArray(), activeFirst, false);
            int retained = Math.Min(overlap, active.Count);
            float[] tail = active.GetRange(active.Count - retained, retained).ToArray();
            active.Clear(); active.AddRange(tail); activeFirst = samplesSeen - retained;
            voicedSamples = voiced ? retained : 0; silentSamples = voiced ? 0 : retained;
            return [result];
        }
        return [];
    }

    public SpeechWindow? Flush()
    {
        if (active.Count == 0 || voicedSamples < minimumVoiced) { ResetActive(); return null; }
        var result = new SpeechWindow(active.ToArray(), activeFirst, true); ResetActive(); return result;
    }
    public void Reset() { preRoll.Clear(); ResetActive(); samplesSeen = 0; hasTimeline = false; }
    private void ResetActive() { active.Clear(); voicedSamples = silentSamples = 0; }
    private static int Seconds(double value) => checked((int)Math.Round(value * SampleRate));
    private static float Rms(float[] samples)
    {
        double sum = 0; foreach (float sample in samples) { float safe = float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0; sum += safe * safe; }
        return (float)Math.Sqrt(sum / samples.Length);
    }
    private static void AddBounded(List<float> target, float[] values, int limit)
    {
        target.AddRange(values);
        if (target.Count > limit) target.RemoveRange(0, target.Count - limit);
    }
}
