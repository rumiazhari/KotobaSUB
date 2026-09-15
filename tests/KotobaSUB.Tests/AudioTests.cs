using KotobaSUB.Core.Audio;

internal static class AudioTests
{
    private static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
    private static AudioBlock Block(float value, int samples, long first = -1) => new(Enumerable.Repeat(value, samples).ToArray(), 16000, first);
    private static TranscriptionSegment Segment(string text, float probability = .8f, float noSpeech = .1f) => new(text, TimeSpan.Zero, TimeSpan.FromSeconds(1), probability, noSpeech);
    public static void Run(Action<string, Action> test)
    {
        test("PCM conversion downmixes and resamples continuously", () =>
        {
            var bytes = new byte[480 * 2 * 2];
            for (int frame = 0; frame < 480; frame++)
            {
                short left = (short)(frame * 40 - 9000), right = (short)(9000 - frame * 20);
                BitConverter.GetBytes(left).CopyTo(bytes, frame * 4); BitConverter.GetBytes(right).CopyTo(bytes, frame * 4 + 2);
            }
            var whole = new AudioSampleConverter(48000, 2, PcmSampleEncoding.Signed16).Convert(bytes);
            var splitConverter = new AudioSampleConverter(48000, 2, PcmSampleEncoding.Signed16);
            var split = splitConverter.Convert(bytes.AsSpan(0, 960)).Concat(splitConverter.Convert(bytes.AsSpan(960))).ToArray();
            Check(whole.Length == 160 && split.Length == whole.Length);
            Check(whole.Zip(split).All(pair => Math.Abs(pair.First - pair.Second) < .00001f));
            Check(whole.All(float.IsFinite) && whole.All(v => v is >= -1 and <= 1));
            try { new AudioSampleConverter(48000, 2, PcmSampleEncoding.Signed16).Convert(new byte[3]); throw new Exception("Expected alignment rejection"); } catch (InvalidDataException) { }
            byte[] invalidFloat = BitConverter.GetBytes(float.NaN); Check(new AudioSampleConverter(16000, 1, PcmSampleEncoding.Float32).Convert(invalidFloat).Single() == 0);
        });
        test("model installer verifies before atomic replacement", () => RunAsync(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "KotobaSUB-model-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
            try
            {
                byte[] payload = System.Text.Encoding.UTF8.GetBytes("verified model fixture");
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
                var handler = new StaticHttpHandler(payload); using var client = new HttpClient(handler);
                string target = Path.Combine(directory, "model.bin"); File.WriteAllText(target, "prior");
                var installer = new WhisperModelInstaller("https://fixture.invalid/model", hash, 1024);
                var result = await installer.InstallAsync(client, target);
                Check(!result.AlreadyPresent && File.ReadAllBytes(target).SequenceEqual(payload) && !File.Exists(target + ".download"));
                var reused = await installer.InstallAsync(client, target);
                Check(reused.AlreadyPresent && handler.Requests == 1);
            }
            finally { Directory.Delete(directory, true); }
        }));
        test("model installer preserves prior file on checksum or size failure", () => RunAsync(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "KotobaSUB-model-failure-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
            try
            {
                string target = Path.Combine(directory, "model.bin"); File.WriteAllText(target, "prior");
                using var client = new HttpClient(new StaticHttpHandler([1, 2, 3, 4]));
                var installer = new WhisperModelInstaller("https://fixture.invalid/model", new string('0', 64), 3);
                try { await installer.InstallAsync(client, target); throw new Exception("Expected bounded download failure"); } catch (InvalidDataException) { }
                Check(File.ReadAllText(target) == "prior" && !File.Exists(target + ".download"));
            }
            finally { Directory.Delete(directory, true); }
        }));        test("model installer cancellation removes partial file and preserves prior", () => RunAsync(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "KotobaSUB-model-cancel-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
            try
            {
                string target = Path.Combine(directory, "model.bin"); File.WriteAllText(target, "prior");
                byte[] payload = Enumerable.Repeat((byte)7, 128).ToArray(); string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
                using var client = new HttpClient(new SlowHttpHandler(payload)); using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                try { await new WhisperModelInstaller("https://fixture.invalid/model", hash, 1024).InstallAsync(client, target, cancellationToken: cancel.Token); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
                Check(File.ReadAllText(target) == "prior" && !File.Exists(target + ".download"));
            }
            finally { Directory.Delete(directory, true); }
        }));        test("activity gate rejects silence and short noise", () =>
        {
            var gate = new SpeechActivityGate();
            for (int i = 0; i < 10; i++) Check(gate.Push(Block(0, 1600)).Count == 0);
            Check(gate.Push(Block(.003f, 1600)).Count == 0);
            Check(gate.Push(Block(.1f, 1600)).Count == 0);
            for (int i = 0; i < 7; i++) Check(gate.Push(Block(0, 1600)).Count == 0);
            Check(gate.Flush() is null);
        });
        test("activity gate closes speech after silence with pre-roll", () =>
        {
            var gate = new SpeechActivityGate();
            gate.Push(Block(0, 3200));
            gate.Push(Block(.08f, 3200)); gate.Push(Block(.08f, 3200));
            IReadOnlyList<SpeechWindow> output = [];
            for (int i = 0; i < 6; i++) output = gate.Push(Block(0, 1600));
            var window = output.Single();
            Check(window.FinalAfterSilence && window.Samples.Length >= 16000);
            Check(window.Samples.Any(s => s == 0) && window.Samples.Any(s => s > 0));
        });
        test("activity gate bounds long speech and overlaps windows", () =>
        {
            var gate = new SpeechActivityGate(maximumWindowSeconds: 2, overlapSeconds: .5);
            var outputs = new List<SpeechWindow>(); long position = 0;
            for (int i = 0; i < 30; i++) { outputs.AddRange(gate.Push(Block(.05f, 1600, position))); position += 1600; }
            Check(outputs.Count >= 1 && outputs.All(w => w.Samples.Length <= 32000));
            Check(!outputs[0].FinalAfterSilence);
            var tail = gate.Flush(); Check(tail is not null && tail.FirstSample < position);
            gate.Reset(); gate.Push(Block(.05f, 4800, 1000)); gate.Push(Block(.05f, 4800, 20000)); Check(gate.Flush()?.FirstSample == 20000);
        });
        test("stable transcript confirms partials and suppresses overlap", () =>
        {
            var stable = new StableTranscript();
            Check(stable.Submit([Segment("今日は天気")], false) is null);
            Check(stable.Submit([Segment("今日は天気です")], false) == "今日は天気");
            Check(stable.Submit([Segment("今日は天気です")], true) == "です");
            Check(stable.Submit([Segment("です")], true) is null);
            stable.SilenceReset(); Check(stable.Submit([Segment("です")], true) == "です");
        });
        test("transcription session emits stable speech and stops cleanly", () => RunAsync(async () =>
        {
            var source = new FakeSource([
                Block(0, 3200), Block(.08f, 3200), Block(.08f, 3200),
                Block(0, 1600), Block(0, 1600), Block(0, 1600), Block(0, 1600), Block(0, 1600), Block(0, 1600)]);
            var provider = new FakeTranscriber();
            await using var session = new AudioTranscriptionSession(source, provider, _ => { });
            var completion = new TaskCompletionSource<TranscriptionSegment>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Transcript += value => completion.TrySetResult(value);
            await session.StartAsync();
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(result.Text == "こんにちは" && provider.MaxConcurrent == 1);
            source.Report(new(AudioSourceHealth.Faulted, "device removed"));
            Check(session.Status.Health == AudioSourceHealth.Faulted);
            await session.StopAsync(); Check(source.Stopped && session.Status.Health == AudioSourceHealth.Stopped);
        }));
        test("transcription session flushes a short utterance after input inactivity", () => RunAsync(async () =>
        {
            var source = new FakeSource([Block(.08f, 4800)]);
            var provider = new FakeTranscriber();
            await using var session = new AudioTranscriptionSession(source, provider, _ => { }, TimeSpan.FromMilliseconds(20));
            var completion = new TaskCompletionSource<TranscriptionSegment>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Transcript += value => completion.TrySetResult(value);
            await session.StartAsync();
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Check(result.Text == "こんにちは" && result.Final);
        }));        test("transcription session cancels after missing model", () => RunAsync(async () =>
        {
            var source = new FakeSource([Block(.08f, 4800), Block(0, 9600)]);
            await using var session = new AudioTranscriptionSession(source, new MissingTranscriber(), _ => { });
            var unavailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.StatusChanged += value => { if (value.Health == AudioSourceHealth.Unavailable) unavailable.TrySetResult(); };
            await session.StartAsync(); await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(session.Status.Health == AudioSourceHealth.Unavailable);
            await session.StopAsync(); Check(source.Stopped);
        }));
        test("stable transcript rejects low confidence and music markers", () =>
        {
            var stable = new StableTranscript();
            Check(stable.Submit([Segment("幻覚", .2f)], true) is null);
            Check(stable.Submit([Segment("幻覚", .9f, .8f)], true) is null);
            Check(stable.Submit([Segment("[音楽]")], true) is null);
            Check(stable.Submit([Segment("ご視聴ありがとうございました")], true) is null);
            Check(stable.Submit([Segment("  私 は 学校 です  ")], true) == "私は学校です");
        });
    }
    private static void RunAsync(Func<Task> action) => action().GetAwaiter().GetResult();
    private sealed class StaticHttpHandler(byte[] payload) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++; var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            return Task.FromResult(response);
        }
    }    private sealed class SlowHttpHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new SlowReadStream(payload)) });
    }
    private sealed class SlowReadStream(byte[] payload) : Stream
    {
        private bool sent;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => payload.Length; public override long Position { get => sent ? payload.Length : 0; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!sent) { sent = true; int count = Math.Min(buffer.Length, payload.Length / 2); payload.AsMemory(0, count).CopyTo(buffer); return ValueTask.FromResult(count); }
            return new(Task.Run(async () => { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }, cancellationToken));
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }    private sealed class FakeSource(IReadOnlyList<AudioBlock> blocks) : IAudioSource
    {
        public AudioSourceStatus Status { get; private set; } = new(AudioSourceHealth.Stopped, "stopped");
        public long DroppedBlocks => 0;
        public bool Stopped { get; private set; }
        public event Action<AudioSourceStatus>? StatusChanged;
        public Task StartAsync(CancellationToken cancellationToken) { Report(new(AudioSourceHealth.Running, "fixture")); return Task.CompletedTask; }
        public async IAsyncEnumerable<AudioBlock> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var block in blocks) { cancellationToken.ThrowIfCancellationRequested(); yield return block; await Task.Yield(); }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public Task StopAsync() { Stopped = true; Report(new(AudioSourceHealth.Stopped, "stopped")); return Task.CompletedTask; }
        public void Report(AudioSourceStatus value) { Status = value; StatusChanged?.Invoke(value); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeTranscriber : ITranscriptionProvider
    {
        private int concurrent;
        public int MaxConcurrent { get; private set; }
        public async Task<IReadOnlyList<TranscriptionSegment>> TranscribeAsync(SpeechWindow window, CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref concurrent); MaxConcurrent = Math.Max(MaxConcurrent, active);
            try { await Task.Delay(10, cancellationToken); return [Segment("こんにちは")]; }
            finally { Interlocked.Decrement(ref concurrent); }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class MissingTranscriber : ITranscriptionProvider
    {
        public Task<IReadOnlyList<TranscriptionSegment>> TranscribeAsync(SpeechWindow window, CancellationToken cancellationToken) => throw new FileNotFoundException("model missing");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }}
