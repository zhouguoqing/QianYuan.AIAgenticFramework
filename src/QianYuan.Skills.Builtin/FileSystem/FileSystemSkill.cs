using System.Text.Json;
using System.Text.Json.Nodes;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;

namespace QianYuan.Skills.Builtin.FileSystem;

/// <summary>
/// Sandboxed filesystem skill. All paths are resolved relative to a configured root and refused
/// when they would escape it. Provides read/write/list/glob tools.
/// </summary>
public sealed class FileSystemSkill : ISkill
{
    private readonly string _rootFull;
    private readonly bool _readOnly;

    public FileSystemSkill(string rootDirectory, bool readOnly = false)
    {
        _rootFull = Path.GetFullPath(rootDirectory);
        _readOnly = readOnly;
        Directory.CreateDirectory(_rootFull);
    }

    public string Id => "qianyuan.fs";
    public string Name => "File System";
    public string Description => $"Read{(_readOnly ? "" : "/write")} files within {_rootFull}.";
    public IReadOnlyList<string> Tags => new[] { "file", "filesystem", "io", "read", "write" };
    public string? SystemPromptFragment => $"Sandbox root: {_rootFull}. All paths are interpreted relative to it; absolute paths and traversal are rejected.";

    private ToolDefinition[] BuildTools()
    {
        var tools = new List<ToolDefinition>
        {
            new ToolDefinition
            {
                Name = "fs_read",
                Description = "Read a UTF-8 text file (relative to sandbox root).",
                JsonSchema = """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""
            },
            new ToolDefinition
            {
                Name = "fs_list",
                Description = "List files under a directory (relative to sandbox root).",
                JsonSchema = """{"type":"object","properties":{"path":{"type":"string","default":"."},"glob":{"type":"string","description":"Optional glob pattern, e.g. **/*.cs"}}}"""
            }
        };
        if (!_readOnly)
        {
            tools.Add(new ToolDefinition
            {
                Name = "fs_write",
                Description = "Write a UTF-8 text file (relative to sandbox root). Overwrites existing.",
                JsonSchema = """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}"""
            });
        }
        return tools.ToArray();
    }

    public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
        => ValueTask.FromResult<IReadOnlyList<ToolDefinition>>(BuildTools());

    public async ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string argumentsJson, SkillInvocationContext context, CancellationToken ct = default)
    {
        var args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) ?? new JsonObject();
        try
        {
            return toolName switch
            {
                "fs_read" => await ReadAsync(args, ct).ConfigureAwait(false),
                "fs_list" => List(args),
                "fs_write" => _readOnly
                    ? SkillInvocationResult.Error("filesystem is read-only")
                    : await WriteAsync(args, ct).ConfigureAwait(false),
                _ => SkillInvocationResult.Error($"unknown tool '{toolName}'")
            };
        }
        catch (Exception ex)
        {
            return SkillInvocationResult.Error(ex.Message);
        }
    }

    private async Task<SkillInvocationResult> ReadAsync(JsonNode args, CancellationToken ct)
    {
        var path = Resolve(args["path"]?.GetValue<string>() ?? "");
        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return SkillInvocationResult.Ok(
            JsonSerializer.Serialize(new { path, content = text }),
            $"read {Path.GetRelativePath(_rootFull, path)} ({text.Length} chars)");
    }

    private async Task<SkillInvocationResult> WriteAsync(JsonNode args, CancellationToken ct)
    {
        var path = Resolve(args["path"]?.GetValue<string>() ?? "");
        var content = args["content"]?.GetValue<string>() ?? "";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
        return SkillInvocationResult.Ok(
            JsonSerializer.Serialize(new { path, bytes = System.Text.Encoding.UTF8.GetByteCount(content) }),
            $"wrote {Path.GetRelativePath(_rootFull, path)}");
    }

    private SkillInvocationResult List(JsonNode args)
    {
        var rel = args["path"]?.GetValue<string>() ?? ".";
        var glob = args["glob"]?.GetValue<string>();
        var dir = Resolve(rel);
        if (!Directory.Exists(dir)) return SkillInvocationResult.Error($"directory not found: {rel}");

        IEnumerable<string> files;
        if (string.IsNullOrEmpty(glob))
            files = Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.TopDirectoryOnly);
        else
        {
            var matcher = new Microsoft.Extensions.FileSystemGlobbing.Matcher();
            matcher.AddInclude(glob);
            var rooted = new Microsoft.Extensions.FileSystemGlobbing.Abstractions.DirectoryInfoWrapper(new DirectoryInfo(dir));
            files = matcher.Execute(rooted).Files.Select(f => Path.Combine(dir, f.Path));
        }
        var entries = files.Select(f => Path.GetRelativePath(_rootFull, f)).Take(500).ToArray();
        return SkillInvocationResult.Ok(
            JsonSerializer.Serialize(new { root = _rootFull, entries }),
            $"{entries.Length} entries");
    }

    private string Resolve(string rel)
    {
        if (Path.IsPathRooted(rel)) throw new InvalidOperationException("absolute paths are not allowed");
        var full = Path.GetFullPath(Path.Combine(_rootFull, rel));
        if (!full.StartsWith(_rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("path escapes sandbox root");
        return full;
    }
}
