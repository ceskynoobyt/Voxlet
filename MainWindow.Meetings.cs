using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.Win32;
using NAudio.Wave;

namespace Voxlet;

public sealed partial class MainWindow
{
    MeetingAudioCapture? meetingCapture;
    bool meetingRecording, meetingProcessing;
    bool meetingChatBusy;
    MeetingEntry? activeMeeting;
    StackPanel? meetingTranscript;
    TextBlock? meetingState;
    TextBlock? meetingPreview;
    DispatcherTimer? meetingPreviewTimer;
    Task? meetingPreviewTask;
    bool meetingPreviewBusy;

    void MeetingsPage()
    {
        MeetingAudioCapture.CleanupAbandoned();
        body.Children.Clear();
        body.Children.Add(SectionIntro("Notetaker", settings.SpeechMode == "On-device" ? "Record microphone and Windows audio. Voxlet transcribes locally and deletes temporary audio after saving the transcript." : "Record microphone and Windows audio. Both tracks are sent to the selected speech provider, then temporary audio is deleted after saving."));
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var startButton = new Button { Content = "●   Start meeting", Style = (Style)FindResource("AppButton"), Margin = new Thickness(0, 0, 8, 0), IsEnabled = !meetingRecording && !meetingProcessing };
        startButton.Click += (_, _) => StartMeeting();
        var stopButton = new Button { Content = "Stop and transcribe", Style = (Style)FindResource("AppButton"), IsEnabled = meetingRecording };
        stopButton.Click += (_, _) => FinishMeeting();
        actions.Children.Add(startButton); actions.Children.Add(stopButton); body.Children.Add(Card(actions));
        meetingState = new TextBlock { Text = meetingRecording ? "Recording microphone + Windows system audio…" : meetingProcessing ? "Transcribing meeting audio…" : "Ready · audio is stored temporarily only while a meeting is being processed.", Foreground = B("#A0A4A7"), Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        body.Children.Add(meetingState);
        if (!SpeakerDiarizationService.IsInstalled)
        {
            var modelCard = new StackPanel(); modelCard.Children.Add(new TextBlock { Text = "Offline speaker labels", FontSize = 16, FontWeight = FontWeights.SemiBold }); modelCard.Children.Add(new TextBlock { Text = "Optional local model pack · about 45 MB · downloaded from the sherpa-onnx project. No audio is uploaded for speaker labeling.", Foreground = B("#A0A4A7"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
            var install = new Button { Content = "Download speaker model", Style = (Style)FindResource("AppButton"), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !meetingRecording && !meetingProcessing }; install.Click += async (_, _) => await InstallDiarizationModels(); modelCard.Children.Add(install); body.Children.Add(Card(modelCard));
        }
        var live = new StackPanel(); live.Children.Add(new TextBlock { Text = activeMeeting?.Title ?? "Live transcript", FontSize = 18, FontWeight = FontWeights.SemiBold });
        meetingPreview = new TextBlock { Text = meetingRecording ? "Listening. Local preview will appear as audio chunks are processed." : "", TextWrapping = TextWrapping.Wrap, Foreground = B("#DDE2EA"), Margin = new Thickness(0, 8, 0, 0) }; live.Children.Add(meetingPreview);
        meetingTranscript = live; if (activeMeeting is not null) RenderMeetingSegments(activeMeeting, live); else live.Children.Add(new TextBlock { Text = "Your transcript will appear here after you stop the meeting.", Foreground = B("#A0A4A7"), Margin = new Thickness(0, 8, 0, 0) });
        var liveCard = Card(live);
        if (activeMeeting is null) body.Children.Add(liveCard);
        if (activeMeeting is not null)
        {
            var chat = new StackPanel(); chat.Children.Add(new TextBlock { Text = "Ask about this meeting", FontSize = 18, FontWeight = FontWeights.SemiBold });
            chat.Children.Add(new TextBlock { Text = "Grounded in this transcript and its selected sources. A cloud assistant receives this context with your question.", Foreground = B("#A0A4A7"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) });
            foreach (var message in activeMeeting.Chat) chat.Children.Add(new TextBlock { Text = (message.Role == "user" ? "You: " : "Voxlet: ") + message.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            var question = new TextBox { MinHeight = 66, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6), ToolTip = "This question and the meeting context go to the configured assistant provider." };
            var ask = new Button { Content = "Ask", Style = (Style)FindResource("AppButton"), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !meetingRecording && !meetingProcessing && !meetingChatBusy && vault.Meetings.Any(x => x.Id == activeMeeting.Id) };
            ask.Click += async (_, _) => await AskAboutMeeting(activeMeeting, question.Text); chat.Children.Add(question); chat.Children.Add(ask);
            var detail = new Grid(); detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) }); detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var chatCard = Card(chat); Grid.SetColumn(liveCard, 0); Grid.SetColumn(chatCard, 1); detail.Children.Add(liveCard); detail.Children.Add(chatCard); body.Children.Add(detail);
            var attach = new Button { Content = "Attach .txt or .md file", Style = (Style)FindResource("AppButton"), HorizontalAlignment = HorizontalAlignment.Left };
            attach.Click += (_, _) => AttachMeetingFile(activeMeeting); body.Children.Add(attach);
            var attachMcp = new Button { Content = "Attach MCP resources", Style = (Style)FindResource("AppButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0), IsEnabled = settings.McpServers.Count > 0 };
            attachMcp.ToolTip = settings.McpServers.Count > 0 ? "Choose specific read-only resources for this meeting." : "Configure a local MCP server in Settings first.";
            attachMcp.Click += async (_, _) => await AttachMcpResources(activeMeeting); body.Children.Add(attachMcp);
            foreach (var source in activeMeeting.Sources) body.Children.Add(new TextBlock { Text = "Attached · " + source.Name, Foreground = B("#A0A4A7"), Margin = new Thickness(0, 5, 0, 0) });
        }
        body.Children.Add(new TextBlock { Text = "Previous meetings", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });
        if (vault.Meetings.Count > 0) { var clear = new Button { Content = "Delete all meeting notes", Style = (Style)FindResource("AppButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 9) }; clear.Click += (_, _) => { if (GlassDialog.Show(this, "Delete every saved meeting transcript, attachment, and chat from this device?", "Confirm deletion", true)) { vault.Meetings.Clear(); activeMeeting = null; VaultStore.Save(vault); MeetingsPage(); } }; body.Children.Add(clear); }
        foreach (var meeting in vault.Meetings.OrderByDescending(x => x.Created))
        {
            var row = new StackPanel(); row.Children.Add(new TextBlock { Text = meeting.Title, FontWeight = FontWeights.SemiBold }); row.Children.Add(new TextBlock { Text = $"{meeting.Created.LocalDateTime:g} · {TimeSpan.FromSeconds(meeting.DurationSeconds):m\\:ss} · {meeting.Segments.Count} transcript segments", Foreground = B("#A0A4A7"), Margin = new Thickness(0, 3, 0, 0) });
            var select = new Button { Content = "Open", Style = (Style)FindResource("AppButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) }; select.Click += (_, _) => { activeMeeting = meeting; MeetingsPage(); }; row.Children.Add(select); body.Children.Add(Card(row));
        }
    }

    void StartMeeting()
    {
        if (meetingRecording || meetingProcessing) return;
        try
        {
            var micIndex = (micBox.SelectedItem as MicChoice)?.Index ?? -1;
            meetingCapture = new MeetingAudioCapture(); meetingCapture.Start(micIndex); meetingRecording = true; activeMeeting = new MeetingEntry { Title = "Meeting · " + DateTime.Now.ToString("g"), Language = settings.AutoDetectLanguage ? "auto" : settings.SpokenLanguage };
            status.Text = "Recording meeting · microphone + Windows system audio"; MeetingsPage(); StartMeetingPreview();
        }
        catch (Exception ex) { meetingRecording = false; meetingCapture = null; status.Text = "Could not start meeting capture: " + ex.Message; GlassDialog.Show(this, "Voxlet could not start both audio sources. Check your microphone and Windows output device.\n\n" + ex.Message, "Meeting capture failed"); MeetingsPage(); }
    }

    async void FinishMeeting()
    {
        if (!meetingRecording || meetingCapture is null || activeMeeting is null) return;
        meetingRecording = false; meetingProcessing = true; meetingPreviewTimer?.Stop(); MeetingsPage();
        MeetingAudioFiles? files = null;
        try
        {
            files = await meetingCapture.StopAsync(); meetingCapture = null; if (meetingPreviewTask is not null) await meetingPreviewTask;
            var language = settings.AutoDetectLanguage ? "auto" : settings.SpokenLanguage;
            var duration = Math.Max(files.Duration.TotalSeconds, .1); activeMeeting.DurationSeconds = duration;
            var micSegments = await TranscribeMeetingTrack(files.MicrophoneWav, "you", "You", duration, language);
            var systemSegments = await TranscribeMeetingTrack(files.SystemWav, "speaker-1", "Speaker 1", duration, language);
            activeMeeting.Segments.AddRange(micSegments); activeMeeting.Segments.AddRange(systemSegments);
            if (activeMeeting.Segments.Count == 0) throw new InvalidOperationException("No speech was recognized. The meeting audio was discarded.");
            activeMeeting.Segments = activeMeeting.Segments.OrderBy(x => x.StartSeconds).ToList(); ApplySourceSpeakerNames(activeMeeting);
            vault.Meetings.Add(activeMeeting); VaultStore.Save(vault); status.Text = "Meeting transcript saved on this device.";
        }
        catch (Exception ex) { status.Text = "Meeting transcription failed. Temporary audio will be deleted. " + ex.Message; GlassDialog.Show(this, ex.Message, "Meeting transcription failed"); }
        finally
        {
            if (files is not null) MeetingAudioCapture.DeleteSessionFiles(files.Directory);
            if (meetingCapture is not null) { await meetingCapture.DisposeAsync(); meetingCapture = null; }
            meetingRecording = false; meetingProcessing = false; MeetingsPage();
        }
    }

    void CancelMeetingCapture()
    {
        meetingRecording = false; meetingProcessing = false; var capture = meetingCapture; meetingCapture = null;
        if (capture is not null) _ = capture.DisposeAsync(); activeMeeting = null; MeetingsPage();
    }

    async Task<List<MeetingSegment>> TranscribeMeetingTrack(string path, string speakerId, string speakerName, double duration, string language)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 5000) return [];
        var normalized = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-16k.wav");
        using (var audioReader = new AudioFileReader(path)) using (var resampler = new MediaFoundationResampler(audioReader, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 }) WaveFileWriter.CreateWaveFile(normalized, resampler);
        try
        {
            List<MeetingSegment> segments;
            if (settings.SpeechMode == "On-device")
            {
                var output = normalized + ".final";
                var model = SelectedLocalWhisperPath(); var cli = settings.PerformanceMode != "CPU" && File.Exists(LocalPaths.WhisperGpuCli) ? LocalPaths.WhisperGpuCli : LocalPaths.WhisperCli;
                var result = await RunWhisperVtt(cli, normalized, model, language, output); if (result.ExitCode != 0 && cli == LocalPaths.WhisperGpuCli) result = await RunWhisperVtt(LocalPaths.WhisperCli, normalized, model, language, output);
                if (result.ExitCode != 0) throw new InvalidOperationException("Speech engine error: " + result.Error);
                var cues = ParseVtt(output + ".vtt", duration); segments = cues.Select(c => new MeetingSegment { StartSeconds = c.Start, EndSeconds = c.End, SpeakerId = speakerId, SpeakerName = speakerName, Text = Clean(c.Text) }).Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
                try { File.Delete(output + ".vtt"); } catch { }
            }
            else { var text = Clean(await CloudTranscription.TranscribeAsync(settings, normalized, language)); segments = string.IsNullOrWhiteSpace(text) ? [] : [new MeetingSegment { StartSeconds = 0, EndSeconds = duration, SpeakerId = speakerId, SpeakerName = speakerName, Text = text }]; }
            if (speakerId != "you" && SpeakerDiarizationService.IsInstalled)
            {
                try { var voices = await SpeakerDiarizationService.ProcessAsync(normalized); foreach (var segment in segments) { var voice = voices.OrderByDescending(v => Math.Max(0, Math.Min(segment.EndSeconds, v.EndSeconds) - Math.Max(segment.StartSeconds, v.StartSeconds))).FirstOrDefault(); if (voice is not null && Math.Min(segment.EndSeconds, voice.EndSeconds) > Math.Max(segment.StartSeconds, voice.StartSeconds)) { segment.SpeakerId = "speaker-" + (voice.Speaker + 1); segment.SpeakerName = "Speaker " + (voice.Speaker + 1); } } }
                catch (Exception ex) { status.Text = "Speaker labeling was unavailable; the transcript is still saved. " + ex.Message; }
            }
            return segments;
        }
        finally { try { File.Delete(normalized); } catch { } }
    }

    static List<(double Start, double End, string Text)> ParseVtt(string path, double duration)
    {
        if (!File.Exists(path)) return [];
        var matches = Regex.Matches(File.ReadAllText(path), @"(?m)^(\d{2}:\d{2}:\d{2}\.\d{3})\s+-->\s+(\d{2}:\d{2}:\d{2}\.\d{3}).*\r?\n([\s\S]*?)(?=\r?\n\r?\n|\z)");
        return matches.Select(m => (ParseTime(m.Groups[1].Value), Math.Min(duration, ParseTime(m.Groups[2].Value)), Regex.Replace(m.Groups[3].Value.Trim(), @"\s+", " "))).Where(x => x.Item2 > x.Item1 && x.Item3.Length > 0).ToList();
        static double ParseTime(string value) { var p = value.Split(new[] { ':', '.' }); return int.Parse(p[0]) * 3600 + int.Parse(p[1]) * 60 + int.Parse(p[2]) + int.Parse(p[3]) / 1000d; }
    }

    static async Task<(int ExitCode, string Error)> RunWhisperVtt(string cli, string wav, string model, string language, string output)
    {
        var psi = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        var threads = string.Equals(cli, LocalPaths.WhisperGpuCli, StringComparison.OrdinalIgnoreCase) ? Math.Min(Environment.ProcessorCount, 8) : Environment.ProcessorCount;
        foreach (var arg in new[] { "-m", model, "-f", wav, "-l", language, "-t", threads.ToString(), "-nt", "-np", "-ovtt", "-of", output }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start local speech engine."); var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); return (process.ExitCode, (await stderr) + "\n" + (await stdout));
    }

    void RenderMeetingSegments(MeetingEntry meeting, StackPanel target)
    {
        foreach (var segment in meeting.Segments)
        {
            var row = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            var name = new TextBox { Text = segment.SpeakerName, Tag = segment.SpeakerId, Width = 112, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = B("#DDE2EA"), FontWeight = FontWeights.SemiBold, Padding = new Thickness(0), ToolTip = "Edit this speaker label" };
            name.LostFocus += (_, _) => { var label = name.Text.Trim(); if (label.Length == 0) return; foreach (var s in meeting.Segments.Where(x => x.SpeakerId == (string)name.Tag)) { s.SpeakerName = label; s.SpeakerNameEvidence = "manually renamed"; } VaultStore.Save(vault); };
            heading.Children.Add(name); heading.Children.Add(new TextBlock { Text = " · " + TimeSpan.FromSeconds(segment.StartSeconds).ToString(@"m\:ss"), FontWeight = FontWeights.SemiBold, Foreground = B("#DDE2EA"), VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(heading);
            if (!string.IsNullOrWhiteSpace(segment.SpeakerNameEvidence)) row.Children.Add(new TextBlock { Text = "Name from " + segment.SpeakerNameEvidence, FontSize = 10, Foreground = B("#A0A4A7") });
            row.Children.Add(new TextBlock { Text = segment.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }); target.Children.Add(row);
        }
    }

    void AttachMeetingFile(MeetingEntry meeting)
    {
        var dialog = new OpenFileDialog { Filter = "Meeting notes (*.txt;*.md)|*.txt;*.md", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var info = new FileInfo(dialog.FileName); if (info.Length > 1_000_000) throw new InvalidDataException("Attachments are limited to 1 MB of plain text.");
            var text = File.ReadAllText(dialog.FileName); if (text.Contains('\0')) throw new InvalidDataException("This file is not plain text.");
            meeting.Sources.Add(new MeetingSourceSnapshot { Name = info.Name, Uri = info.FullName, Text = text }); ApplySourceSpeakerNames(meeting); VaultStore.Save(vault); MeetingsPage();
        }
        catch (Exception ex) { GlassDialog.Show(this, ex.Message, "Could not attach file"); }
    }

    static void ApplySourceSpeakerNames(MeetingEntry meeting)
    {
        foreach (var source in meeting.Sources)
            foreach (Match match in System.Text.RegularExpressions.Regex.Matches(source.Text, @"(?im)^\s*Speaker\s*(\d+)\s*[:=—-]\s*([\p{L}][\p{L} .'-]{0,60})\s*$"))
            {
                var id = "speaker-" + int.Parse(match.Groups[1].Value); var label = match.Groups[2].Value.Trim();
                foreach (var segment in meeting.Segments.Where(s => s.SpeakerId == id)) { segment.SpeakerName = label; segment.SpeakerNameEvidence = source.Name; }
            }
        foreach (var segment in meeting.Segments.Where(s => s.SpeakerId != "you" && s.SpeakerName.StartsWith("Speaker ", StringComparison.Ordinal)))
        {
            var intro = Regex.Match(segment.Text, @"\b(?:my name is|this is)\s+([\p{L}][\p{L}'’-]*(?:\s+[\p{L}][\p{L}'’-]*){0,2})", RegexOptions.IgnoreCase);
            if (intro.Success) { segment.SpeakerName = intro.Groups[1].Value.Trim(); segment.SpeakerNameEvidence = "self-introduction at " + TimeSpan.FromSeconds(segment.StartSeconds).ToString(@"m\:ss"); }
        }
    }

    async Task AskAboutMeeting(MeetingEntry meeting, string question)
    {
        question = question.Trim(); if (question.Length == 0 || meetingChatBusy) return;
        if (!vault.Meetings.Any(x => x.Id == meeting.Id)) { status.Text = "Finish and save the meeting before asking questions."; return; }
        meetingChatBusy = true; meeting.Chat.Add(new MeetingChatMessage { Role = "user", Text = question }); VaultStore.Save(vault); MeetingsPage();
        try { var answer = await MeetingAssistantService.AskAsync(settings, meeting, question); meeting.Chat.Add(new MeetingChatMessage { Role = "assistant", Text = answer }); VaultStore.Save(vault); }
        catch (Exception ex) { meeting.Chat.RemoveAt(meeting.Chat.Count - 1); GlassDialog.Show(this, ex.Message, "Meeting assistant unavailable"); }
        finally { meetingChatBusy = false; MeetingsPage(); }
    }

    async Task AttachMcpResources(MeetingEntry meeting)
    {
        if (settings.McpServers.Count == 0) return;
        var picker = new Window { Title = "Attach meeting sources", Owner = this, Width = 620, Height = 510, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = ThemeColors.Get(settings.Theme).Brush(), ResizeMode = ResizeMode.NoResize };
        var root = new StackPanel { Margin = new Thickness(22) }; root.Children.Add(new TextBlock { Text = "Choose MCP resources", FontSize = 20, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = "Only checked resources will be read and attached to this meeting.", TextWrapping = TextWrapping.Wrap, Foreground = B("#A0A4A7"), Margin = new Thickness(0, 5, 0, 12) });
        var servers = new ComboBox { ItemsSource = settings.McpServers, DisplayMemberPath = "Name", SelectedIndex = 0, Height = 38 }; root.Children.Add(servers);
        var state = new TextBlock { Text = "Choose a server to load its resource list.", Foreground = B("#A0A4A7"), Margin = new Thickness(0, 8, 0, 5) }; root.Children.Add(state);
        var resourceList = new StackPanel(); var scroll = new ScrollViewer { Content = resourceList, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll);
        async Task LoadResources()
        {
            if (servers.SelectedItem is not McpServerConnection server) return;
            resourceList.Children.Clear(); state.Text = "Connecting to " + server.Name + "…";
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var items = await MeetingMcpService.ListResourcesAsync(server, timeout.Token);
                state.Text = items.Count == 0 ? "This server did not expose read-only resources." : $"{items.Count} resources · check the ones to include.";
                foreach (var item in items) resourceList.Children.Add(new CheckBox { Content = item.Name + (string.IsNullOrWhiteSpace(item.Description) ? "" : " · " + item.Description), Tag = item, Margin = new Thickness(3, 6, 3, 6), ToolTip = item.Uri, Foreground = B("#DDE2EA") });
            }
            catch (Exception ex) { state.Text = "Connection failed: " + ex.Message; }
        }
        servers.SelectionChanged += async (_, _) => await LoadResources();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("AppButton"), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => picker.DialogResult = false;
        var add = new Button { Content = "Attach selected", Style = (Style)FindResource("AppButton") }; add.Click += async (_, _) =>
        {
            if (servers.SelectedItem is not McpServerConnection server) return;
            var selected = resourceList.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (MeetingMcpResource)x.Tag).ToArray(); if (selected.Length == 0) { state.Text = "Select at least one resource."; return; }
            add.IsEnabled = false; state.Text = "Reading selected resources…";
            try
            {
                foreach (var item in selected) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var text = await MeetingMcpService.ReadResourceAsync(server, item.Uri, timeout.Token); meeting.Sources.Add(new MeetingSourceSnapshot { Kind = "mcp", Name = server.Name + " · " + item.Name, Uri = item.Uri, Text = text }); }
                VaultStore.Save(vault); picker.DialogResult = true;
            }
            catch (Exception ex) { state.Text = "Could not read a selected resource: " + ex.Message; add.IsEnabled = true; }
        };
        footer.Children.Add(cancel); footer.Children.Add(add); root.Children.Add(footer); picker.Content = new Border { CornerRadius = new CornerRadius(16), BorderBrush = (Brush)Application.Current.Resources["GlassPanelEdge"], BorderThickness = new Thickness(1), Padding = new Thickness(5), Child = root };
        picker.Loaded += async (_, _) => await LoadResources(); if (picker.ShowDialog() == true) MeetingsPage();
    }

    void StartMeetingPreview()
    {
        meetingPreviewTimer?.Stop(); meetingPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        meetingPreviewTimer.Tick += (_, _) => { if (!meetingPreviewBusy && meetingRecording && meetingCapture is not null) meetingPreviewTask = PreviewMeetingChunk(); };
        meetingPreviewTimer.Start();
        if (PreviewModel() is null && meetingPreview is not null) meetingPreview.Text = "Live preview needs a compatible local Whisper model. Final transcription will use your selected speech provider after you stop.";
    }

    string? PreviewModel()
    {
        var language = settings.AutoDetectLanguage ? "auto" : settings.SpokenLanguage;
        var selected = SelectedLocalWhisperPath(language: language, auto: settings.AutoDetectLanguage);
        if (File.Exists(selected)) return selected;
        if (language == "en" && File.Exists(LocalPaths.EnglishModel)) return LocalPaths.EnglishModel;
        return null;
    }

    async Task PreviewMeetingChunk()
    {
        var capture = meetingCapture; var meeting = activeMeeting; var model = PreviewModel();
        if (capture is null || meeting is null || model is null || meetingPreviewBusy) return;
        meetingPreviewBusy = true;
        try
        {
            var dir = Path.Combine(LocalPaths.MeetingTemp, $"meeting-{Environment.ProcessId}-{Guid.NewGuid():N}"); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, ".voxlet-owner"), Environment.ProcessId.ToString());
            var mic = Path.Combine(dir, "mic.wav"); var sys = Path.Combine(dir, "system.wav");
            try
            {
                var gotMic = capture.WritePreview(true, mic); var gotSystem = capture.WritePreview(false, sys);
                var language = settings.AutoDetectLanguage ? "auto" : settings.SpokenLanguage; var parts = new List<string>();
                if (gotMic) parts.Add(await TranscribePreviewTrack(mic, model, language));
                if (gotSystem) parts.Add(await TranscribePreviewTrack(sys, model, language));
                var text = string.Join("\n", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
                await Dispatcher.InvokeAsync(() => { if (meetingRecording && meetingPreview is not null && text.Length > 0) meetingPreview.Text = text; });
            }
            finally { MeetingAudioCapture.DeleteSessionFiles(dir); }
        }
        catch (Exception ex) { await Dispatcher.InvokeAsync(() => { if (meetingPreview is not null) meetingPreview.Text = "Live preview is unavailable: " + ex.Message; }); }
        finally { meetingPreviewBusy = false; }
    }

    async Task<string> TranscribePreviewTrack(string path, string model, string language)
    {
        var input = path + ".16k.wav"; var output = path + ".preview"; var textFile = output + ".txt";
        try
        {
            using (var reader = new AudioFileReader(path)) using (var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 }) WaveFileWriter.CreateWaveFile(input, resampler);
            var cli = settings.PerformanceMode != "CPU" && File.Exists(LocalPaths.WhisperGpuCli) ? LocalPaths.WhisperGpuCli : LocalPaths.WhisperCli;
            var result = await RunWhisper(cli, input, model, language, output); if (result.ExitCode != 0 && cli == LocalPaths.WhisperGpuCli) result = await RunWhisper(LocalPaths.WhisperCli, input, model, language, output);
            if (result.ExitCode != 0) return ""; return File.Exists(textFile) ? Clean(await File.ReadAllTextAsync(textFile)) : "";
        }
        finally { foreach (var file in new[] { input, textFile }) try { File.Delete(file); } catch { } }
    }

    async Task InstallDiarizationModels()
    {
        try { var progress = new Progress<string>(message => { status.Text = message; if (meetingState is not null) meetingState.Text = message; }); await SpeakerDiarizationService.DownloadModelsAsync(progress); MeetingsPage(); }
        catch (Exception ex) { GlassDialog.Show(this, ex.Message, "Speaker model download failed"); }
    }
}
