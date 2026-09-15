using System.Net.Http;
using System.Security.Cryptography;

namespace KotobaSUB.Core.Audio;

public sealed record ModelInstallProgress(long BytesReceived, long? TotalBytes);
public sealed record ModelInstallResult(bool AlreadyPresent, long Bytes, string Sha256);

public sealed class WhisperModelInstaller
{
    public const string DefaultUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin?download=true";
    public const string DefaultSha256 = "60ED5BC3DD14EEA856493D334349B405782DDCAF0028D4B5DF4088345FBA2EFE";
    public const long DefaultMaximumBytes = 160_000_000;
    private readonly string url;
    private readonly string expectedSha256;
    private readonly long maximumBytes;

    public WhisperModelInstaller(string? url = null, string? expectedSha256 = null, long maximumBytes = DefaultMaximumBytes)
    {
        this.url = url ?? DefaultUrl;
        this.expectedSha256 = (expectedSha256 ?? DefaultSha256).ToUpperInvariant();
        if (this.expectedSha256.Length != 64 || !this.expectedSha256.All(Uri.IsHexDigit)) throw new ArgumentException("Expected SHA256 must be 64 hexadecimal characters", nameof(expectedSha256));
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.maximumBytes = maximumBytes;
    }

    public async Task<ModelInstallResult> InstallAsync(HttpClient client, string targetPath, IProgress<ModelInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        string target = Path.GetFullPath(targetPath);
        string temporary = target + ".download";
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            string current = await HashFileAsync(target, cancellationToken).ConfigureAwait(false);
            if (current == expectedSha256) return new(true, new FileInfo(target).Length, current);
        }
        if (File.Exists(temporary)) File.Delete(temporary);
        try
        {
            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? expectedLength = response.Content.Headers.ContentLength;
            if (expectedLength > maximumBytes) throw new InvalidDataException($"Whisper model exceeds {maximumBytes} bytes");
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920]; long received = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                received += read;
                if (received > maximumBytes) throw new InvalidDataException($"Whisper model exceeds {maximumBytes} bytes");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read); progress?.Report(new(received, expectedLength));
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            string actual = Convert.ToHexString(hash.GetHashAndReset());
            if (actual != expectedSha256) throw new InvalidDataException($"Whisper model checksum mismatch: {actual}");
            output.Close(); File.Move(temporary, target, true);
            return new(false, received, actual);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}