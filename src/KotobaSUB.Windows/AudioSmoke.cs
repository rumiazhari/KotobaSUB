using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using KotobaSUB.Core.Audio;
using KotobaSUB.Windows.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace KotobaSUB.Windows;

internal static class AudioSmoke
{
    public static int Run(string[] args)
    {
        try { return RunAsync(args).GetAwaiter().GetResult(); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static async Task<int> RunAsync(string[] args)
    {
        string Value(string key, string fallback) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        string model = Path.GetFullPath(Value("--model", ".data/models/ggml-base.bin"));
        string fixture = Path.GetFullPath(Value("--fixture", ".data/fixtures/konnichiwa.mp3"));
        string output = Path.GetFullPath(Value("--output", "artifacts/audio-smoke"));
        Directory.CreateDirectory(output); var messages = new List<string>(); void Log(string value) { messages.Add(value); Console.WriteLine(value); }
        float[] samples = Decode(fixture); Log($"Decoded Japanese fixture: {samples.Length / 16000d:F2}s");
        var watch = Stopwatch.StartNew(); IReadOnlyList<TranscriptionSegment> transcript;
        await using (var whisper = new WhisperTranscriber(model, Log)) transcript = await whisper.TranscribeAsync(new(samples, 0, true), CancellationToken.None);
        string text = string.Concat(transcript.Select(s => s.Text)).Trim();
        Check(text.Contains("こんにちは") || text.Contains("今日は"), $"Unexpected Japanese transcript: {text}");
        Check(transcript.Count > 0 && transcript.All(s => s.Probability >= 0 && s.Probability <= 1), "Whisper confidence missing");
        double inferenceMilliseconds = watch.Elapsed.TotalMilliseconds;
        Log($"PASS real CPU Japanese inference: {text}; {inferenceMilliseconds:F0}ms");

        int captured = 0; double peak = 0; int restartCaptured = 0; double restartPeak = 0;
        await using (var source = new WasapiLoopbackAudioSource())
        {
            source.StatusChanged += value => Log($"Capture {value.Health}: {value.Message}");
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                await source.StartAsync(timeout.Token);
                using var player = new global::Windows.Media.Playback.MediaPlayer { Volume = .25, AutoPlay = false, Source = MediaSource.CreateFromUri(new Uri(fixture)) };
                player.Play();
                await foreach (var block in source.ReadAllAsync(timeout.Token))
                {
                    captured += block.Samples.Length; peak = Math.Max(peak, block.Samples.Max(Math.Abs));
                    if (peak > .002 && captured >= 6400) break;
                }
                player.Pause(); await source.StopAsync();
                Check(source.DroppedBlocks == 0, "Loopback queue dropped blocks in first smoke cycle");
            }
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                await source.StartAsync(timeout.Token);
                using var player = new global::Windows.Media.Playback.MediaPlayer { Volume = .25, AutoPlay = false, Source = MediaSource.CreateFromUri(new Uri(fixture)) };
                player.Play();
                await foreach (var block in source.ReadAllAsync(timeout.Token))
                {
                    restartCaptured += block.Samples.Length; restartPeak = Math.Max(restartPeak, block.Samples.Max(Math.Abs));
                    if (restartPeak > .002 && restartCaptured >= 6400) break;
                }
                player.Pause(); await source.StopAsync();
                Check(source.DroppedBlocks == 0, "Loopback queue dropped blocks after restart");
            }
        }
        Check(peak > .002 && restartPeak > .002, "WASAPI loopback did not capture the played fixture in both cycles");
        Log($"PASS real WASAPI loopback and restart: first={captured / 16000d:F2}s/{peak:F4}, second={restartCaptured / 16000d:F2}s/{restartPeak:F4}");
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { text, inferenceMilliseconds, capturedSamples = captured, peak, restartCapturedSamples = restartCaptured, restartPeak, messages }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) }));
        return 0;
    }
    private static float[] Decode(string path)
    {
        using var reader = new MediaFoundationReader(path);
        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels == 2) samples = new StereoToMonoSampleProvider(samples);
        else if (samples.WaveFormat.Channels != 1) throw new NotSupportedException($"Fixture has {samples.WaveFormat.Channels} channels");
        if (samples.WaveFormat.SampleRate != 16000) samples = new WdlResamplingSampleProvider(samples, 16000);
        var result = new List<float>(); float[] buffer = new float[4096]; int read;
        while ((read = samples.Read(buffer.AsSpan())) > 0) { result.AddRange(buffer.AsSpan(0, read).ToArray()); if (result.Count > 16000 * 30) throw new InvalidDataException("Fixture exceeds 30 seconds"); }
        return result.ToArray();
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
