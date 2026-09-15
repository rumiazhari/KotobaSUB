using System.Diagnostics;
using KotobaSUB.Core.Audio;

namespace KotobaSUB.Core;

public enum SubtitleSourceKind { None, StructuredLyrics, LocalAsr }
public sealed record SourceRoutingDecision(SubtitleSourceKind Source, SubtitleFrame Frame, bool ShouldRunAsr, TimeSpan? NextEvaluation);

public sealed class SubtitleSourceRouter(TimeSpan? fallbackDelay = null, TimeSpan? transcriptLifetime = null)
{
    private readonly TimeSpan delay = fallbackDelay ?? TimeSpan.FromMilliseconds(750);
    private readonly TimeSpan lifetime = transcriptLifetime ?? TimeSpan.FromSeconds(4);
    private long? missingSince;
    private SubtitleLine? transcript;
    private long transcriptReceived;
    private bool transcriptWasFinal = true;

    public void AcceptTranscript(TranscriptionSegment value, long timestamp, bool asrEnabled = true)
    {
        if (!asrEnabled) { ClearTranscript(); return; }
        string text = value.Text.Trim();
        if (text.Length == 0) return;
        if (transcript is not null && !transcriptWasFinal)
            text = transcript.OriginalText + text;
        transcript = new SubtitleLine(value.Start, value.End, text, [], null, TimingPrecision.Segment);
        transcriptReceived = timestamp;
        transcriptWasFinal = value.Final;
    }

    public void ClearTranscript() { transcript = null; transcriptWasFinal = true; }

    public SourceRoutingDecision Evaluate(bool suspended, bool hasStructuredTimeline, SubtitleFrame structured, AudioSourceHealth asrHealth, long timestamp, bool asrEnabled = true)
    {
        if (suspended)
        {
            missingSince = null; ClearTranscript();
            return new(SubtitleSourceKind.None, new(null), false, null);
        }
        if (hasStructuredTimeline)
        {
            missingSince = null; ClearTranscript();
            return new(SubtitleSourceKind.StructuredLyrics, structured, false, null);
        }
        if (!asrEnabled)
        {
            missingSince = null; ClearTranscript();
            return new(SubtitleSourceKind.None, new(null), false, null);
        }
        missingSince ??= timestamp;
        if (asrHealth is AudioSourceHealth.Unavailable or AudioSourceHealth.Faulted) ClearTranscript();
        bool canRun = asrHealth is AudioSourceHealth.Stopped or AudioSourceHealth.Starting or AudioSourceHealth.Running;
        TimeSpan elapsed = Elapsed(missingSince.Value, timestamp);
        bool shouldRun = canRun && elapsed >= delay;
        TimeSpan? next = shouldRun ? null : canRun ? delay - elapsed : null;
        if (transcript is not null)
        {
            TimeSpan age = Elapsed(transcriptReceived, timestamp);
            if (age < lifetime)
            {
                TimeSpan remaining = lifetime - age;
                next = next is null || remaining < next ? remaining : next;
                return new(SubtitleSourceKind.LocalAsr, new(transcript), shouldRun, next);
            }
            ClearTranscript();
        }
        return new(SubtitleSourceKind.None, new(null), shouldRun, next);
    }

    private static TimeSpan Elapsed(long start, long end) => TimeSpan.FromSeconds(Math.Max(0, end - start) / (double)Stopwatch.Frequency);
}