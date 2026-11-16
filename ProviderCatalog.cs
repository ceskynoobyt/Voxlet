namespace Voxlet;

public sealed record ProviderModel(string Id,string Name,string Detail="",bool Ready=true);
public sealed record SpeechProvider(string Id,string Name,ProviderModel[] Models,bool NeedsKey=true);

public static class ProviderCatalog
{
    public static readonly SpeechProvider[] Cloud = [
        new("openwhispr","OpenWhispr Cloud",[new("cloud","OpenWhispr Cloud","Sign in · no API key")],false),
        new("openai","OpenAI",[new("gpt-transcribe","GPT Transcribe · Newest"),new("gpt-4o-mini-transcribe","GPT-4o Mini Transcribe · Fast"),new("gpt-4o-transcribe","GPT-4o Transcribe · Most accurate"),new("whisper-1","Whisper · Original")]),
        new("groq","Groq",[new("whisper-large-v3","Whisper Large v3"),new("whisper-large-v3-turbo","Whisper Large v3 Turbo")]),
        new("xai","xAI",[new("grok-voice-transcribe-2.0","Grok STT")]),
        new("mistral","Mistral",[new("voxtral-mini-transcribe-3","Voxtral Mini")]),
        new("gemini","Gemini",[new("gemini-3.5-transcribe","Gemini 3.5 Transcribe"),new("gemini-3.5-transcribe-live","Gemini 3.5 Transcribe Live","Real-time · live stream only",false)]),
        new("corti","Corti",[new("corti-transcribe","Corti Transcribe · Medical-grade")]),
        new("tinfoil","Tinfoil",[new("voxtral-mini-transcribe","Voxtral · verified private","Attested transport integration pending",false)]),
        new("deepgram","Deepgram",[new("nova-3","Nova-3")]),
        new("assemblyai","AssemblyAI",[new("universal-3-5-pro","Universal-3.5 Pro"),new("universal-streaming-english","Universal Streaming English","Streaming only · live stream integration pending",false),new("universal-streaming-multilingual","Universal Streaming Multilingual","Streaming only · live stream integration pending",false)]),
        new("custom","Custom",[new("custom","OpenAI-compatible audio/transcriptions")])
    ];

    public static readonly SpeechProvider[] Local = [
        new("oruk","Oruk",[new("orukeet-v0.1.0","Orukeet","25 languages · runtime integration pending",false)],false),
        new("openai-local","OpenAI",[new("tiny","Whisper Tiny · 75 MB · Fastest"),new("base","Whisper Base · 142 MB · Fast"),new("small","Whisper Small · 466 MB · Accurate"),new("medium","Whisper Medium · 1.5 GB · High accuracy"),new("large-v3","Whisper Large v3 · 3 GB · Best"),new("large-v3-turbo","Whisper Turbo · 1.6 GB · Fast & accurate")],false),
        new("nvidia","NVIDIA",[new("parakeet-tdt-0.6b-v3","Parakeet TDT 0.6B","25 languages · runtime integration pending",false),new("parakeet-unified-en-0.6b","Parakeet Unified EN 0.6B","English · runtime integration pending",false),new("nemotron-speech-streaming-en-0.6b","Nemotron Speech Streaming EN 0.6B","English · runtime integration pending",false),new("nemotron-3.5-asr-streaming-0.6b","Nemotron 3.5 ASR Streaming 0.6B","Multilingual · runtime integration pending",false)],false),
        new("cohere","Cohere",[new("cohere-transcribe-2b","Cohere Transcribe 2B","14 languages · runtime integration pending",false)],false)
    ];

    public static readonly SpeechProvider[] Enterprise = [new("azure","Azure OpenAI",[new("gpt-transcribe","GPT Transcribe"),new("gpt-4o-mini-transcribe","GPT-4o Mini Transcribe")])];
    public static SpeechProvider[] Providers(string mode)=>mode switch{"Cloud"=>Cloud,"Enterprise Cloud"=>Enterprise,_=>Local};
}
