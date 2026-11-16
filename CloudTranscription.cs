using System.Net.Http.Headers;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Voxlet;

public static class CloudTranscription
{
    static readonly HttpClient Http=new(){Timeout=TimeSpan.FromMinutes(3)};
    public static async Task<string> TranscribeAsync(AppSettings settings,string wav,string language)
    {
        var provider=settings.SpeechProvider;var key=SecretStore.Get((settings.SpeechMode=="Enterprise Cloud"?"enterprise:":"cloud:")+provider);
        if(provider=="openwhispr")throw new InvalidOperationException("OpenWhispr Cloud requires an OpenWhispr account sign-in. Its public docs do not provide a third-party desktop sign-in or speech endpoint. Use OpenWhispr's official app, or select a BYOK provider here.");
        if(provider=="tinfoil")throw new InvalidOperationException("Tinfoil's speech API requires its attested SecureClient transport. Voxlet does not yet have that runtime, so it will not send your audio over an unattested connection.");
        if(provider=="corti")return await Corti(settings,wav,language,SecretStore.Get("corti-client-secret"));
        if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException($"Add and save your {ProviderCatalog.Providers(settings.SpeechMode).FirstOrDefault(x=>x.Id==provider)?.Name??provider} API key in AI Models settings first.");
        return provider switch
        {
            "openai"=>await OpenAiCompatible("https://api.openai.com/v1/audio/transcriptions",key,settings.SpeechModel,wav,language),
            "groq"=>await OpenAiCompatible("https://api.groq.com/openai/v1/audio/transcriptions",key,settings.SpeechModel,wav,language),
            "mistral"=>await OpenAiCompatible("https://api.mistral.ai/v1/audio/transcriptions",key,settings.SpeechModel,wav,language,false),
            "xai"=>await Xai(key,wav,language),
            "gemini"=>await Gemini(key,wav,language,settings.SpeechModel),
            "deepgram"=>await Deepgram(key,wav,language),
            "assemblyai"=>await Assembly(key,wav,language),
            "custom"=>await OpenAiCompatible(BuildCustomUrl(settings.CustomEndpoint),key,string.IsNullOrWhiteSpace(settings.CustomModel)?settings.SpeechModel:settings.CustomModel,wav,language),
            "azure"=>await Azure(settings,wav,language,key),
            _=>throw new InvalidOperationException("This speech provider is not available yet.")
        };
    }

    static async Task<string> OpenAiCompatible(string url,string key,string model,string wav,string language,bool responseFormat=true)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,url);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);using var form=new MultipartFormDataContent();form.Add(new StringContent(model),"model");if(language!="auto")form.Add(new StringContent(language),"language");if(responseFormat)form.Add(new StringContent("json"),"response_format");var audio=new ByteArrayContent(await File.ReadAllBytesAsync(wav));audio.Headers.ContentType=new MediaTypeHeaderValue("audio/wav");form.Add(audio,"file",Path.GetFileName(wav));request.Content=form;return await ReadText(request,"Speech provider");
    }

    static async Task<string> Xai(string key,string wav,string language)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.x.ai/v1/stt");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);using var form=new MultipartFormDataContent();form.Add(new StringContent("grok-voice-transcribe-2.0"),"model");form.Add(new StringContent("false"),"filler_words");if(language!="auto"){form.Add(new StringContent(language),"language");form.Add(new StringContent("true"),"format");}var audio=new ByteArrayContent(await File.ReadAllBytesAsync(wav));audio.Headers.ContentType=new MediaTypeHeaderValue("audio/wav");form.Add(audio,"file",Path.GetFileName(wav));request.Content=form;return await ReadText(request,"xAI");
    }

    static async Task<string> Gemini(string key,string wav,string language,string model)
    {
        var bytes=await File.ReadAllBytesAsync(wav);if(bytes.Length>14*1024*1024)throw new InvalidOperationException("Gemini's inline audio request limit is 14 MB. Use another provider for this long dictation.");var body=new Dictionary<string,object>{{"model",model},{"input",new[]{new{type="audio",data=Convert.ToBase64String(bytes),mime_type="audio/wav"}}}};if(language!="auto")body["generation_config"]=new{transcription_config=new{language_codes=new[]{language}}};using var req=new HttpRequestMessage(HttpMethod.Post,"https://generativelanguage.googleapis.com/v1beta/interactions");req.Headers.Add("x-goog-api-key",key);req.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");using var response=await Http.SendAsync(req);var text=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Gemini: "+ErrorText(text));using var json=JsonDocument.Parse(text);var root=json.RootElement;if(root.TryGetProperty("status",out var state)&&state.GetString()!="completed")throw new InvalidOperationException("Gemini transcription did not complete: "+text);if(root.TryGetProperty("output_text",out var outText))return outText.GetString()??"";if(root.TryGetProperty("steps",out var steps))return string.Join(" ",steps.EnumerateArray().SelectMany(x=>x.TryGetProperty("content",out var c)?c.EnumerateArray():[]).Where(x=>x.TryGetProperty("type",out var t)&&t.GetString()=="text").Select(x=>x.GetProperty("text").GetString()));return "";
    }

    static async Task<string> Deepgram(string key,string wav,string language)
    {
        var lang=language=="auto"?"multi":Uri.EscapeDataString(language);var url=$"https://api.deepgram.com/v1/listen?model=nova-3&language={lang}&mip_opt_out=true&smart_format=true";using var req=new HttpRequestMessage(HttpMethod.Post,url);req.Headers.Authorization=new AuthenticationHeaderValue("Token",key);var audio=new ByteArrayContent(await File.ReadAllBytesAsync(wav));audio.Headers.ContentType=new MediaTypeHeaderValue("audio/wav");req.Content=audio;using var response=await Http.SendAsync(req);var body=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Deepgram: "+ErrorText(body));using var json=JsonDocument.Parse(body);return json.RootElement.GetProperty("results").GetProperty("channels")[0].GetProperty("alternatives")[0].GetProperty("transcript").GetString()??"";
    }

    static async Task<string> Assembly(string key,string wav,string language)
    {
        using var req=new HttpRequestMessage(HttpMethod.Post,"https://sync.assemblyai.com/transcribe");req.Headers.Add("Authorization",key);req.Headers.Add("X-AAI-Model",settingsModel(language));using var form=new MultipartFormDataContent();var audio=new ByteArrayContent(await File.ReadAllBytesAsync(wav));audio.Headers.ContentType=new MediaTypeHeaderValue("audio/wav");form.Add(audio,"audio",Path.GetFileName(wav));if(language!="auto")form.Add(new StringContent(JsonSerializer.Serialize(new{language_code=language})),"config");req.Content=form;using var response=await Http.SendAsync(req);var body=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new InvalidOperationException("AssemblyAI: "+ErrorText(body));using var json=JsonDocument.Parse(body);return json.RootElement.TryGetProperty("text",out var t)?t.GetString()??"":"";
        static string settingsModel(string _)=>"universal-3-5-pro";
    }

    static async Task<string> Corti(AppSettings settings,string wav,string language,string clientSecret)
    {
        if(string.IsNullOrWhiteSpace(settings.CortiClientId)||string.IsNullOrWhiteSpace(clientSecret))throw new InvalidOperationException("Corti needs a client ID and client secret in AI Models settings.");var env=settings.CortiEnvironment is "us"?"us":"eu";var tenant=Uri.EscapeDataString(string.IsNullOrWhiteSpace(settings.CortiTenant)?"base":settings.CortiTenant);using var auth=new HttpRequestMessage(HttpMethod.Post,$"https://auth.{env}.corti.app/realms/{tenant}/protocol/openid-connect/token");auth.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"grant_type","client_credentials"},{"client_id",settings.CortiClientId},{"client_secret",clientSecret},{"scope","openid"}});using var tokenResponse=await Http.SendAsync(auth);var tokenBody=await tokenResponse.Content.ReadAsStringAsync();if(!tokenResponse.IsSuccessStatusCode)throw new InvalidOperationException("Corti authentication: "+ErrorText(tokenBody));using var tokenJson=JsonDocument.Parse(tokenBody);var token=tokenJson.RootElement.GetProperty("access_token").GetString()!;var baseUrl=$"https://api.{env}.corti.app/v2";string? interaction=null;try{var created=await CortiJson(baseUrl+"/interactions/",token,settings.CortiTenant,"POST",JsonSerializer.Serialize(new{encounter=new{identifier="voxlet-"+Guid.NewGuid().ToString("N"),status="completed",type="consultation"}}));interaction=created.GetProperty("interactionId").GetString();var audio=await File.ReadAllBytesAsync(wav);using var record=new HttpRequestMessage(HttpMethod.Post,$"{baseUrl}/interactions/{interaction}/recordings/");record.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);record.Headers.Add("Tenant-Name",settings.CortiTenant);record.Content=new ByteArrayContent(audio);record.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");using var recordResponse=await Http.SendAsync(record);var recordBody=await recordResponse.Content.ReadAsStringAsync();if(!recordResponse.IsSuccessStatusCode)throw new InvalidOperationException("Corti recording: "+ErrorText(recordBody));using var recordJson=JsonDocument.Parse(recordBody);var recording=recordJson.RootElement.GetProperty("recordingId").GetString();var transcript=await CortiJson(baseUrl+$"/interactions/{interaction}/transcripts/",token,settings.CortiTenant,"POST",JsonSerializer.Serialize(new{recordingId=recording,primaryLanguage=language=="auto"?"en":language,isDictation=true}));return string.Join(" ",transcript.GetProperty("transcripts").EnumerateArray().Select(x=>x.GetProperty("text").GetString()));}finally{if(interaction is not null){using var delete=new HttpRequestMessage(HttpMethod.Delete,baseUrl+"/interactions/"+interaction);delete.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);delete.Headers.Add("Tenant-Name",settings.CortiTenant);try{using var _=await Http.SendAsync(delete);}catch{}}}
    }

    static async Task<JsonElement> CortiJson(string url,string token,string tenant,string method,string body){using var req=new HttpRequestMessage(new HttpMethod(method),url);req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);req.Headers.Add("Tenant-Name",tenant);req.Content=new StringContent(body,Encoding.UTF8,"application/json");using var response=await Http.SendAsync(req);var result=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Corti: "+ErrorText(result));using var doc=JsonDocument.Parse(result);return doc.RootElement.Clone();}

    static async Task<string> Azure(AppSettings settings,string wav,string language,string key)
    {
        var endpoint=settings.EnterpriseEndpoint.Trim().TrimEnd('/');if(!Uri.TryCreate(endpoint,UriKind.Absolute,out _))throw new InvalidOperationException("Enter your Azure OpenAI endpoint in AI Models settings.");var deployment=Uri.EscapeDataString(settings.EnterpriseDeployment);var url=$"{endpoint}/openai/deployments/{deployment}/audio/transcriptions?api-version=2025-03-01-preview";using var req=new HttpRequestMessage(HttpMethod.Post,url);req.Headers.Add("api-key",key);using var form=new MultipartFormDataContent();form.Add(new StringContent(settings.SpeechModel),"model");if(language!="auto")form.Add(new StringContent(language),"language");var audio=new ByteArrayContent(await File.ReadAllBytesAsync(wav));audio.Headers.ContentType=new MediaTypeHeaderValue("audio/wav");form.Add(audio,"file",Path.GetFileName(wav));req.Content=form;return await ReadText(req,"Azure OpenAI");
    }

    static string BuildCustomUrl(string endpoint){if(!Uri.TryCreate(endpoint.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="https")throw new InvalidOperationException("Custom endpoints must be a valid HTTPS URL.");return uri.AbsolutePath.TrimEnd('/').EndsWith("/audio/transcriptions",StringComparison.OrdinalIgnoreCase)?endpoint:$"{endpoint.TrimEnd('/')}/audio/transcriptions";}
    static async Task<string> ReadText(HttpRequestMessage request,string provider){using var response=await Http.SendAsync(request);var body=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new InvalidOperationException(provider+": "+ErrorText(body));using var json=JsonDocument.Parse(body);return json.RootElement.TryGetProperty("text",out var text)?text.GetString()??"":throw new InvalidDataException(provider+" returned no transcript text.");}
    static string ErrorText(string body){try{using var json=JsonDocument.Parse(body);if(json.RootElement.TryGetProperty("error",out var error)){if(error.ValueKind==JsonValueKind.Object&&error.TryGetProperty("message",out var message))return message.GetString()??body;if(error.ValueKind==JsonValueKind.String)return error.GetString()??body;}}catch{}return body.Length>600?body[..600]:body;}
}
