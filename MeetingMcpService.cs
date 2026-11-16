using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Voxlet;

public sealed record MeetingMcpResource(string Name, string Uri, string Description);

public static class MeetingMcpService
{
    public static async Task<IReadOnlyList<MeetingMcpResource>> ListResourcesAsync(McpServerConnection server, CancellationToken cancellationToken = default)
    {
        await using var client = await McpClient.CreateAsync(CreateTransport(server), cancellationToken: cancellationToken);
        var resources = await client.ListResourcesAsync(cancellationToken: cancellationToken);
        return resources.Select(r => new MeetingMcpResource(r.Name, r.Uri.ToString(), r.Description ?? "")).ToArray();
    }

    public static async Task<string> ReadResourceAsync(McpServerConnection server, string uri, CancellationToken cancellationToken = default)
    {
        await using var client = await McpClient.CreateAsync(CreateTransport(server), cancellationToken: cancellationToken);
        var result = await client.ReadResourceAsync(uri, cancellationToken: cancellationToken);
        var text = string.Join("\n", result.Contents.OfType<TextResourceContents>().Select(x => x.Text));
        if (text.Length > 100_000) text = text[..100_000] + "\n[Source truncated at 100 KB]";
        return text;
    }

    static StdioClientTransport CreateTransport(McpServerConnection server)
    {
        if (server.Transport != "stdio") throw new InvalidOperationException("Only local stdio MCP servers are enabled in this Voxlet build.");
        if (string.IsNullOrWhiteSpace(server.Command)) throw new InvalidOperationException("Set the MCP server executable or command first.");
        var env = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        foreach (var line in SecretStore.Get("mcp-env:" + server.Id).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = line.IndexOf('='); if (split < 1) continue;
            var name = line[..split].Trim(); if (name.Length == 0 || name.Contains('=') || name.Contains('\0')) continue;
            env[name] = line[(split + 1)..].TrimEnd('\r');
        }
        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Voxlet · " + server.Name,
            Command = server.Command,
            Arguments = server.Arguments.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            WorkingDirectory = string.IsNullOrWhiteSpace(server.WorkingDirectory) ? null : server.WorkingDirectory,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = env,
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        });
    }
}
