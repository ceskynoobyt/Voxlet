using System.IO;
using System.Windows;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Voxlet;

public sealed class AppSettings
{
    public bool SetupComplete { get; set; }
    public string Trigger { get; set; } = "";
    public string SpokenLanguage { get; set; } = "en";
    public string Theme { get; set; } = "Fog";
    public string AppTheme { get; set; } = "Ultraviolet";
    public string MicrophoneId { get; set; } = "default";
    public string PerformanceMode { get; set; } = "Auto";
    public string SpeechMode { get; set; } = "On-device";
    public string SpeechProvider { get; set; } = "openai-local";
    public string SpeechModel { get; set; } = "small";
    public string CustomModel { get; set; } = "whisper-1";
    public string CustomEndpoint { get; set; } = "";
    public string EnterpriseEndpoint { get; set; } = "";
    public string EnterpriseDeployment { get; set; } = "gpt-transcribe";
    public string AssistantProvider { get; set; } = "speech";
    public string AssistantFallbackProvider { get; set; } = "openai";
    public string AssistantModel { get; set; } = "gpt-4o-mini";
    public string AssistantEndpoint { get; set; } = "";
    public string AssistantAzureEndpoint { get; set; } = "";
    public string AssistantAzureDeployment { get; set; } = "";
    public string CortiClientId { get; set; } = "";
    public string CortiTenant { get; set; } = "base";
    public string CortiEnvironment { get; set; } = "eu";
    public bool AutoDetectLanguage { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public bool AutoLearn { get; set; }
    public bool ReviewUncertainWords { get; set; }
    public double TypingWpm { get; set; } = 40;
    public List<McpServerConnection> McpServers { get; set; } = [];
}

public sealed class TranscriptEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public double DurationSeconds { get; set; }
    public double SpeakingWpm { get; set; }
    public string Language { get; set; } = "English";
    public int WordCount { get; set; }
    public string Text { get; set; } = "";
}

public sealed class MeetingEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Title { get; set; } = "Meeting";
    public string Language { get; set; } = "en";
    public double DurationSeconds { get; set; }
    public List<MeetingSegment> Segments { get; set; } = [];
    public List<MeetingSourceSnapshot> Sources { get; set; } = [];
    public List<MeetingChatMessage> Chat { get; set; } = [];
}

public sealed class MeetingSegment
{
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string SpeakerId { get; set; } = "speaker-0";
    public string SpeakerName { get; set; } = "Speaker 1";
    public string SpeakerNameEvidence { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsProvisional { get; set; }
}

public sealed class MeetingSourceSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "file";
    public string Name { get; set; } = "";
    public string Uri { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class MeetingChatMessage
{
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Role { get; set; } = "assistant";
    public string Text { get; set; } = "";
    public List<string> Citations { get; set; } = [];
}

public sealed class McpServerConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Transport { get; set; } = "stdio";
    public string Command { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string Endpoint { get; set; } = "";
}

public static class SpeechLanguages
{
    public static readonly (string Code, string Name)[] Whisper = [
        ("en","English"),("zh","Chinese"),("de","German"),("es","Spanish"),("ru","Russian"),("ko","Korean"),("fr","French"),("ja","Japanese"),("pt","Portuguese"),("tr","Turkish"),("pl","Polish"),("ca","Catalan"),("nl","Dutch"),("ar","Arabic"),("sv","Swedish"),("it","Italian"),("id","Indonesian"),("hi","Hindi"),("fi","Finnish"),("vi","Vietnamese"),("he","Hebrew"),("uk","Ukrainian"),("el","Greek"),("ms","Malay"),("cs","Czech"),("ro","Romanian"),("da","Danish"),("hu","Hungarian"),("ta","Tamil"),("no","Norwegian"),("th","Thai"),("ur","Urdu"),("hr","Croatian"),("bg","Bulgarian"),("lt","Lithuanian"),("la","Latin"),("mi","Maori"),("ml","Malayalam"),("cy","Welsh"),("sk","Slovak"),("te","Telugu"),("fa","Persian"),("lv","Latvian"),("bn","Bengali"),("sr","Serbian"),("az","Azerbaijani"),("sl","Slovenian"),("kn","Kannada"),("et","Estonian"),("mk","Macedonian"),("br","Breton"),("eu","Basque"),("is","Icelandic"),("hy","Armenian"),("ne","Nepali"),("mn","Mongolian"),("bs","Bosnian"),("kk","Kazakh"),("sq","Albanian"),("sw","Swahili"),("gl","Galician"),("mr","Marathi"),("pa","Punjabi"),("si","Sinhala"),("km","Khmer"),("sn","Shona"),("yo","Yoruba"),("so","Somali"),("af","Afrikaans"),("oc","Occitan"),("ka","Georgian"),("be","Belarusian"),("tg","Tajik"),("sd","Sindhi"),("gu","Gujarati"),("am","Amharic"),("yi","Yiddish"),("lo","Lao"),("uz","Uzbek"),("fo","Faroese"),("ht","Haitian Creole"),("ps","Pashto"),("tk","Turkmen"),("nn","Nynorsk"),("mt","Maltese"),("sa","Sanskrit"),("lb","Luxembourgish"),("my","Myanmar"),("bo","Tibetan"),("tl","Tagalog"),("mg","Malagasy"),("as","Assamese"),("tt","Tatar"),("haw","Hawaiian"),("ln","Lingala"),("ha","Hausa"),("ba","Bashkir"),("jw","Javanese"),("su","Sundanese")
    ];
}

public sealed class SpokenShortcut
{
    public string Phrase { get; set; } = "";
    public string Expansion { get; set; } = "";
}

public sealed class UserVault
{
    public List<TranscriptEntry> History { get; set; } = [];
    public List<MeetingEntry> Meetings { get; set; } = [];
    public List<SpokenShortcut> Shortcuts { get; set; } = [];
    public Dictionary<string, List<string>> CustomDictionary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int MilestoneAt { get; set; } = 3000;
    public int TotalWords { get; set; }
    public int TotalDictations { get; set; }
    public Dictionary<string, double> TypingWpmByLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase) { ["English"] = 40 };
}

public static class WordCounter
{
    private static readonly Regex Words = new(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)?", RegexOptions.Compiled);
    public static int Count(string text) => Words.Matches(text).Count;
}

public static class LocalPaths
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voxlet");
    public static readonly string TempAudio = Path.Combine(Root, "temp-audio");
    public static readonly string MeetingTemp = Path.Combine(TempAudio, "meetings");
    public static readonly string DiarizationModels = Path.Combine(Root, "models", "speaker-diarization");
    public static readonly string DiarizationSegmentationModel = Path.Combine(DiarizationModels, "model.onnx");
    public static readonly string DiarizationEmbeddingModel = Path.Combine(DiarizationModels, "speaker-embedding.onnx");
    public static readonly string SettingsFile = Path.Combine(Root, "settings.json");
    public static readonly string VaultFile = Path.Combine(Root, "vault.bin");
    public static string EnglishModel => Path.Combine(AppContext.BaseDirectory, "Resources", "Whisper", "ggml-small.en.bin");
    public static string MultilingualModel => Path.Combine(Root, "models", "ggml-small.bin");
    public static string WhisperModelPath(string model,string language,bool auto){if(model=="small"&&language=="en"&&!auto)return EnglishModel;var id=model switch{"turbo"=>"large-v3-turbo",_=>model};return Path.Combine(Root,"models","ggml-"+id+".bin");}
    public static string WhisperCli => Path.Combine(AppContext.BaseDirectory, "Resources", "Whisper", "cli", "Release", "whisper-cli.exe");
    public static string WhisperGpuCli => Path.Combine(AppContext.BaseDirectory, "Resources", "Whisper", "cuda", "whisper-cli.exe");

    public static void Initialize()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(TempAudio);
        Directory.CreateDirectory(MeetingTemp);
        foreach (var file in Directory.EnumerateFiles(TempAudio))
        {
            try { File.Delete(file); }
            catch { }
        }
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(LocalPaths.SettingsFile)) ?? new(); }
        catch { return new(); }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(LocalPaths.Root);
        File.WriteAllText(LocalPaths.SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
    }
}

public static class SecretStore
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Voxlet-provider-secrets-v1");
    static Dictionary<string,string> Read(){try{return JsonSerializer.Deserialize<Dictionary<string,string>>(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(LocalPaths.Root,"secrets.bin")),Entropy,DataProtectionScope.CurrentUser))??new();}catch{return new();}}
    public static string Get(string name)=>Read().GetValueOrDefault(name,"");
    public static void Set(string name,string value){var values=Read();if(string.IsNullOrWhiteSpace(value))values.Remove(name);else values[name]=value;Directory.CreateDirectory(LocalPaths.Root);File.WriteAllBytes(Path.Combine(LocalPaths.Root,"secrets.bin"),ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(values),Entropy,DataProtectionScope.CurrentUser));}
}

public static class VaultStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Voxlet-local-vault-v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static UserVault Load()
    {
        try
        {
            var encrypted = File.ReadAllBytes(LocalPaths.VaultFile);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<UserVault>(plain, JsonOptions) ?? new();
        }
        catch { return new(); }
    }

    public static void Save(UserVault vault)
    {
        Directory.CreateDirectory(LocalPaths.Root);
        var plain = JsonSerializer.SerializeToUtf8Bytes(vault, JsonOptions);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        var temporary = LocalPaths.VaultFile + ".tmp";
        File.WriteAllBytes(temporary, encrypted);
        File.Move(temporary, LocalPaths.VaultFile, true);
    }
}

public sealed record ThemeColors(Color Start, Color End, Point StartPoint, Point EndPoint, bool DarkText)
{
    public LinearGradientBrush Brush() => new(Start, End, StartPoint, EndPoint);
    public static ThemeColors Get(string name) => name switch
    {
        "Apricot" => new(ColorFrom("#FFE3CC"), ColorFrom("#EE7D52"), new(0, 0), new(1, 1), true),
        "Moss" => new(ColorFrom("#E3E9D6"), ColorFrom("#5F7A52"), new(0, 0), new(1, 1), true),
        "Dusk" => new(ColorFrom("#8D99AE"), ColorFrom("#23253A"), new(0, 0), new(1, 1), false),
        _ => new(ColorFrom("#EEF1F6"), ColorFrom("#A9B4C6"), new(1, 0), new(0.3, 1), true)
    };

    public static Color ColorFrom(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}




