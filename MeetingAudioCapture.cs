using System.Diagnostics;
using System.IO;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace Voxlet;

public sealed record MeetingAudioFiles(string MicrophoneWav, string SystemWav, string Directory, TimeSpan Duration);

public sealed class MeetingAudioCapture : IAsyncDisposable
{
    readonly object micLock = new(), systemLock = new();
    readonly TaskCompletionSource micStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource systemStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Stopwatch clock = new();
    WaveIn? microphone;
    WasapiRecorder? systemAudio;
    WaveFileWriter? micWriter, systemWriter;
    TailBuffer? micTail, systemTail;
    string? folder, micPath, systemPath;
    bool started, micStarted, systemStarted, stopped;

    public void Start(int microphoneIndex)
    {
        if (started) throw new InvalidOperationException("Meeting audio capture has already started.");
        Directory.CreateDirectory(LocalPaths.MeetingTemp);
        folder = Path.Combine(LocalPaths.MeetingTemp, $"meeting-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ".voxlet-owner"), Environment.ProcessId.ToString());
        micPath = Path.Combine(folder, "microphone.wav"); systemPath = Path.Combine(folder, "system.wav");
        try
        {
            microphone = new WaveIn { DeviceNumber = microphoneIndex, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
            micWriter = new WaveFileWriter(micPath, microphone.WaveFormat);
            micTail = new TailBuffer(microphone.WaveFormat, microphone.WaveFormat.AverageBytesPerSecond * 20);
            microphone.DataAvailable += (_, e) => { lock (micLock) { micWriter?.Write(e.Buffer, 0, e.BytesRecorded); micTail?.Append(e.Buffer.AsSpan(0, e.BytesRecorded)); } };
            microphone.RecordingStopped += (_, _) => micStopped.TrySetResult();

            systemAudio = new WasapiRecorderBuilder().WithLoopbackCapture().Build();
            systemWriter = new WaveFileWriter(systemPath, systemAudio.WaveFormat);
            systemTail = new TailBuffer(systemAudio.WaveFormat, systemAudio.WaveFormat.AverageBytesPerSecond * 20);
            systemAudio.DataAvailable += (buffer, _, _, _) => { lock (systemLock) { systemWriter?.Write(buffer); systemTail?.Append(buffer); } };
            systemAudio.RecordingStopped += (_, _) => systemStopped.TrySetResult();

            microphone.StartRecording(); micStarted = true; systemAudio.StartRecording(); systemStarted = true; clock.Start(); started = true;
        }
        catch { try { StopAsync().GetAwaiter().GetResult(); } catch { } DeleteOwnedDirectory(folder); throw; }
    }

    public async Task<MeetingAudioFiles> StopAsync()
    {
        if (stopped) throw new InvalidOperationException("Meeting capture has already been stopped.");
        stopped = true; clock.Stop();
        if (!micStarted) micStopped.TrySetResult(); else try { microphone?.StopRecording(); } catch { micStopped.TrySetResult(); }
        if (!systemStarted) systemStopped.TrySetResult(); else try { systemAudio?.StopRecording(); } catch { systemStopped.TrySetResult(); }
        try { await Task.WhenAll(micStopped.Task, systemStopped.Task).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch { DeleteOwnedDirectory(folder); throw new IOException("Windows audio capture did not stop cleanly. The temporary recording was discarded."); }
        finally
        {
            lock (micLock) { micWriter?.Dispose(); micWriter = null; }
            lock (systemLock) { systemWriter?.Dispose(); systemWriter = null; }
            try { microphone?.Dispose(); } finally { microphone = null; }
            if (systemAudio is not null) { await systemAudio.DisposeAsync(); systemAudio = null; }
        }
        if (!started || folder is null || micPath is null || systemPath is null) throw new InvalidOperationException("Meeting capture did not start.");
        return new(micPath, systemPath, folder, clock.Elapsed);
    }

    public async ValueTask DisposeAsync()
    {
        if (!stopped) try { var files = await StopAsync(); DeleteOwnedDirectory(files.Directory); } catch { if (folder is not null) DeleteOwnedDirectory(folder); }
    }

    public static void CleanupAbandoned()
    {
        Directory.CreateDirectory(LocalPaths.MeetingTemp);
        var root = Path.GetFullPath(LocalPaths.MeetingTemp) + Path.DirectorySeparatorChar;
        foreach (var path in Directory.EnumerateDirectories(LocalPaths.MeetingTemp, "meeting-*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var info = new DirectoryInfo(path); if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || !Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                var parts = info.Name.Split('-'); if (parts.Length != 3 || !int.TryParse(parts[1], out var pid)) continue;
                using var owner = Process.GetProcessById(pid); if (!owner.HasExited) continue;
            }
            catch (ArgumentException) { DeleteOwnedDirectory(path); }
            catch (InvalidOperationException) { DeleteOwnedDirectory(path); }
            catch { }
        }
    }

    static void DeleteOwnedDirectory(string? path)
    {
        try
        {
            if (path is null) return;
            var full = Path.GetFullPath(path); var root = Path.GetFullPath(LocalPaths.MeetingTemp) + Path.DirectorySeparatorChar;
            var info = new DirectoryInfo(full); if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || (info.Attributes & FileAttributes.ReparsePoint) != 0) return;
            var marker = Path.Combine(full, ".voxlet-owner"); if (!File.Exists(marker)) return;
            if (!int.TryParse(File.ReadAllText(marker), out var ownerPid)) return;
            var nameParts = Path.GetFileName(full).Split('-'); if (nameParts.Length != 3 || !int.TryParse(nameParts[1], out var namePid) || namePid != ownerPid) return;
            foreach (var item in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories))
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) return;
            Directory.Delete(full, true);
        }
        catch { }
    }

    public static void DeleteSessionFiles(string? path) => DeleteOwnedDirectory(path);

    public bool WritePreview(bool microphoneTrack, string path, int seconds = 15)
    {
        var tail = microphoneTrack ? micTail : systemTail; if (tail is null) return false;
        var (format, data) = tail.Snapshot(seconds); if (data.Length < format.AverageBytesPerSecond * 2) return false;
        using var writer = new WaveFileWriter(path, format); writer.Write(data, 0, data.Length); return true;
    }

    sealed class TailBuffer(WaveFormat format, int capacity)
    {
        readonly object sync = new(); readonly byte[] data = new byte[capacity]; int writePosition, count;
        public void Append(ReadOnlySpan<byte> chunk)
        {
            lock (sync) { foreach (var value in chunk) { data[writePosition] = value; writePosition = (writePosition + 1) % data.Length; if (count < data.Length) count++; } }
        }
        public (WaveFormat Format, byte[] Data) Snapshot(int seconds)
        {
            lock (sync) { var length = Math.Min(count, format.AverageBytesPerSecond * seconds); length -= length % format.BlockAlign; var start = (writePosition - length + data.Length) % data.Length; var result = new byte[length]; var first = Math.Min(length, data.Length - start); Array.Copy(data, start, result, 0, first); if (first < length) Array.Copy(data, 0, result, first, length - first); return (format, result); }
        }
    }
}
