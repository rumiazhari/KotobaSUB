using System.Diagnostics;

namespace KotobaSUB.Core.Audio;

public sealed record AudioCaptureObservation(long FirstSample, long EndSample, long ObservedAt, bool Discontinuity, long CaptureGeneration = 0);

/// Maps the capture sample timeline emitted by the loopback source to the SMTC
/// media timeline. Capture timestamps are monotonic; inference completion time
/// is never used for lyric evidence.
public sealed class AudioMediaClock
{
    public const int SampleRate = 16000;
    private const double MaximumRate = 8;
    private const double DiscontinuitySeconds = 1.5;
    private readonly object gate = new();
    private MediaSnapshot? latest;
    private string? identity;
    private bool valid;
    private bool reanchor;
    private long anchorSample;
    private long anchorStopwatch;
    private double anchorMediaSeconds;
    private double rate = 1;
    private long lastEndSample = -1;
    private long captureGeneration;

    public bool IsValid { get { lock (gate) return valid; } }

    public void ObserveMedia(MediaSnapshot? snapshot)
    {
        lock (gate)
        {
            if (snapshot?.Track.Identity != identity)
            {
                identity = snapshot?.Track.Identity;
                valid = false; reanchor = false; lastEndSample = -1; captureGeneration = 0;
            }
            if (latest is { } previous && snapshot is { } current && valid)
            {
                if (previous.Playing != current.Playing || Math.Abs(Math.Clamp(previous.Rate, 0, MaximumRate) - Math.Clamp(current.Rate, 0, MaximumRate)) > .01)
                    reanchor = true;
                if (lastEndSample >= 0)
                {
                    double predicted = PredictMediaSeconds(lastEndSample);
                    double actual = current.PositionAt(current.ObservedAt).TotalSeconds;
                    if (Math.Abs(predicted - actual) > DiscontinuitySeconds) reanchor = true;
                }
            }
            latest = snapshot;
        }
    }

    public void ObserveCapture(AudioCaptureObservation observation)
    {
        lock (gate)
        {
            if (latest is null) return;
            bool restart = observation.Discontinuity || !valid || reanchor || observation.FirstSample < anchorSample || observation.CaptureGeneration != captureGeneration;
            if (restart)
            {
                anchorSample = observation.FirstSample;
                anchorStopwatch = observation.ObservedAt;
                anchorMediaSeconds = latest.PositionAt(observation.ObservedAt).TotalSeconds;
                rate = Math.Clamp(latest.Rate, 0, MaximumRate);
                captureGeneration = observation.CaptureGeneration;
                valid = true; reanchor = false;
            }
            lastEndSample = observation.EndSample;
        }
    }

    public TimeSpan? MapCaptureTime(TimeSpan captureTime)
    {
        lock (gate)
        {
            if (!valid || !double.IsFinite(captureTime.TotalSeconds)) return null;
            long sample = (long)Math.Round(captureTime.TotalSeconds * SampleRate);
            return TimeSpan.FromSeconds(Math.Max(0, PredictMediaSeconds(sample)));
        }
    }

    public TimeSpan? EstimateInferenceLatency(TimeSpan captureEnd, long completedAt)
    {
        lock (gate)
        {
            if (!valid) return null;
            long sample = (long)Math.Round(Math.Max(0, captureEnd.TotalSeconds) * SampleRate);
            long expected = anchorStopwatch + (long)Math.Round((sample - anchorSample) / (double)SampleRate * Stopwatch.Frequency);
            return TimeSpan.FromSeconds(Math.Max(0, completedAt - expected) / (double)Stopwatch.Frequency);
        }
    }

    public void Reset()
    {
        lock (gate) { valid = false; reanchor = false; lastEndSample = -1; captureGeneration = 0; latest = null; identity = null; }
    }

    private double PredictMediaSeconds(long sample) => anchorMediaSeconds + (sample - anchorSample) / (double)SampleRate * rate;
}