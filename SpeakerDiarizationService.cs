using System.Diagnostics;
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;
using SherpaOnnx;

namespace Voxlet;

public sealed record DiarizedVoice(double StartSeconds, double EndSeconds, int Speaker);

public static class SpeakerDiarizationService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    public static bool IsInstalled => File.Exists(LocalPaths.DiarizationSegmentationModel) && File.Exists(LocalPaths.DiarizationEmbeddingModel);

    public static async Task DownloadModelsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LocalPaths.DiarizationModels);
        var staging = Path.Combine(LocalPaths.Root, "diarization-download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        var archive = Path.Combine(staging, "segmentation.tar.bz2"); var embedding = Path.Combine(staging, "embedding.onnx");
        try
        {
            progress?.Report("Downloading offline speaker segmentation model…");
            await Download("https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2", archive, 50_000_000, 6_958_444, "24615ee884c897d9d2ba09bb4d30da6bb1b15e685065962db5b02e76e4996488", cancellationToken);
            progress?.Report("Extracting the segmentation model…");
            var extraction = Path.Combine(staging, "extracted"); Directory.CreateDirectory(extraction);
            await RunTar("-xjf", archive, extraction, cancellationToken);
            var segmentation = Directory.EnumerateFiles(extraction, "model.onnx", SearchOption.AllDirectories).FirstOrDefault();
            if (segmentation is null || new FileInfo(segmentation).Length < 1_000_000) throw new InvalidDataException("The official segmentation archive did not contain its expected model file.");
            var license = Directory.EnumerateFiles(extraction, "LICENSE", SearchOption.AllDirectories).FirstOrDefault();
            progress?.Report("Downloading offline speaker embedding model…");
            await Download("https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx", embedding, 100_000_000, 39_593_761, "1a331345f04805badbb495c775a6ddffcdd1a732567d5ec8b3d5749e3c7a5e4b", cancellationToken);
            var segTarget = LocalPaths.DiarizationSegmentationModel + ".download"; var embTarget = LocalPaths.DiarizationEmbeddingModel + ".download";
            File.Copy(segmentation, segTarget, true); File.Copy(embedding, embTarget, true);
            File.Move(segTarget, LocalPaths.DiarizationSegmentationModel, true); File.Move(embTarget, LocalPaths.DiarizationEmbeddingModel, true);
            if (license is not null) File.Copy(license, Path.Combine(LocalPaths.DiarizationModels, "segmentation-LICENSE.txt"), true);
            File.WriteAllText(Path.Combine(LocalPaths.DiarizationModels, "embedding-LICENSE.txt"), "3D-Speaker ERes2Net model: Apache License 2.0. Source project: https://github.com/modelscope/3D-Speaker\nSherpa-ONNX conversion and download source: https://github.com/k2-fsa/sherpa-onnx\n", Encoding.UTF8);
            progress?.Report("Offline speaker diarization is ready.");
        }
        finally { try { Directory.Delete(staging, true); } catch { } }
    }

    public static Task<IReadOnlyList<DiarizedVoice>> ProcessAsync(string wavPath, CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<DiarizedVoice>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsInstalled) throw new InvalidOperationException("Download the optional offline speaker model first.");
        var config = new OfflineSpeakerDiarizationConfig(); config.Segmentation.Pyannote.Model = LocalPaths.DiarizationSegmentationModel; config.Embedding.Model = LocalPaths.DiarizationEmbeddingModel; config.Clustering.Threshold = 0.5f;
        using var diarizer = new OfflineSpeakerDiarization(config); using var audio = new AudioFileReader(wavPath);
        if (diarizer.SampleRate != audio.WaveFormat.SampleRate || audio.WaveFormat.Channels != 1) throw new InvalidDataException("Speaker diarization needs 16 kHz mono audio.");
        var provider = audio.ToSampleProvider(); var samples = new List<float>(); var buffer = new float[8192]; int read; while ((read = provider.Read(buffer.AsSpan())) > 0) { cancellationToken.ThrowIfCancellationRequested(); samples.AddRange(buffer.AsSpan(0, read).ToArray()); }
        return diarizer.Process(samples.ToArray()).Select(x => new DiarizedVoice(x.Start, x.End, x.Speaker)).ToArray();
    }, cancellationToken);

    static async Task Download(string url, string path, long maxBytes, long expectedLength, string expectedSha256, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maxBytes) throw new InvalidDataException("A model download exceeded the allowed size.");
        await using var input = await response.Content.ReadAsStreamAsync(ct); await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        var buffer = new byte[1024 * 1024]; long total = 0;
        while (true) { var read = await input.ReadAsync(buffer, ct); if (read == 0) break; total += read; if (total > maxBytes) throw new InvalidDataException("A model download exceeded the allowed size."); await output.WriteAsync(buffer.AsMemory(0, read), ct); }
        if (total != expectedLength) throw new InvalidDataException("The model download size did not match the official published artifact."); await output.FlushAsync(ct);
        await using var verify = File.OpenRead(path); var actual = Convert.ToHexString(await SHA256.HashDataAsync(verify, ct)).ToLowerInvariant(); if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal)) throw new InvalidDataException("The downloaded model did not pass its SHA-256 integrity check.");
    }

    static async Task RunTar(string operation, string archive, string folder, CancellationToken ct)
    {
        var list = new ProcessStartInfo("tar.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-tjf", archive }) list.ArgumentList.Add(arg);
        using (var process = Process.Start(list) ?? throw new InvalidOperationException("Windows tar.exe is required to install the optional speaker model."))
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(ct); var stderr = await process.StandardError.ReadToEndAsync(ct); await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0) throw new InvalidDataException("Could not inspect the model archive: " + stderr);
            foreach (var item in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = item.Replace('\\', '/'); if (normalized.StartsWith('/') || normalized.Split('/').Any(x => x == "..")) throw new InvalidDataException("The model archive contains an unsafe path.");
            }
        }
        var extract = new ProcessStartInfo("tar.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { operation, archive, "-C", folder }) extract.ArgumentList.Add(arg);
        using var unpack = Process.Start(extract) ?? throw new InvalidOperationException("Could not start the model archive extractor."); var error = await unpack.StandardError.ReadToEndAsync(ct); await unpack.WaitForExitAsync(ct); if (unpack.ExitCode != 0) throw new InvalidDataException("Could not extract the model archive: " + error);
    }
}
