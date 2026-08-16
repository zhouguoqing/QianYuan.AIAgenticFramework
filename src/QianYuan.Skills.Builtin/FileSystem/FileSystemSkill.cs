using System.Text.Json;
using System.Text.Json.Nodes;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Core.Sandbox;

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
    public string? SystemPromptFragment =>
        $"Sandbox root defaults to {_rootFull}. If runtime workspace context is provided, file operations use that workspace root instead. All tool paths are relative and traversal is rejected.";

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
                "fs_read" => await ReadAsync(args, context, ct).ConfigureAwait(false),
                "fs_list" => List(args, context),
                "fs_write" => _readOnly
                    ? SkillInvocationResult.Error("filesystem is read-only")
                    : await WriteAsync(args, context, ct).ConfigureAwait(false),
                _ => SkillInvocationResult.Error($"unknown tool '{toolName}'")
            };
        }
        catch (Exception ex)
        {
            return SkillInvocationResult.Error(ex.Message);
        }
    }

    private async Task<SkillInvocationResult> ReadAsync(JsonNode args, SkillInvocationContext context, CancellationToken ct)
    {
        var root = ResolveRoot(context);
        var path = Resolve(root, args["path"]?.GetValue<string>() ?? "");
        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return SkillInvocationResult.Ok(
            JsonSerializer.Serialize(new { path, content = text }),
            $"read {Path.GetRelativePath(root, path)} ({text.Length} chars)");
    }

    private async Task<SkillInvocationResult> WriteAsync(JsonNode args, SkillInvocationContext context, CancellationToken ct)
    {
        if (!CanWrite(context))
            return SkillInvocationResult.Error("filesystem write is disabled by permission policy for current workspace");

        var root = ResolveRoot(context);
        var path = Resolve(root, args["path"]?.GetValue<string>() ?? "");
        var content = args["content"]?.GetValue<string>() ?? "";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
        return SkillInvocationResult.Ok(
            JsonSerializer.Serialize(new { path, bytes = System.Text.Encoding.UTF8.GetByteCount(content) }),
            $"wrote {Path.GetRelativePath(root, path)}");
    }

    private SkillInvocationResult List(JsonNode args, SkillInvocationContext context)
    {
        var root = ResolveRoot(context);
        var rel = args["path"]?.GetValue<string>() ?? ".";
        var glob = args["glob"]?.GetValue<string>();
        var dir = Resolve(root, rel);
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
        var entries = files.Select(f => Path.GetRelativePath(root, f)).Take(500).ToArray();
        return SkillInvocationResult.Ok(
            JsonSerializer.Serialize(new { root, entries }),
            $"{entries.Length} entries");
    }

    private string ResolveRoot(SkillInvocationContext context)
    {
        var workspaceRoot = context.SandboxPolicy?.WorkspaceRoot
            ?? ResolveMetadataValue(context, "workspacePath");

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            var full = Path.GetFullPath(workspaceRoot);
            Directory.CreateDirectory(full);
            return full;
        }

        return _rootFull;
    }

    private bool CanWrite(SkillInvocationContext context)
    {
        if (_readOnly) return false;

        var policy = context.SandboxPolicy;
        if (policy is not null) return policy.AllowsWrite;

        if (context.Metadata is null) return true;
        if (!context.Metadata.TryGetValue("permission", out var permission) || string.IsNullOrWhiteSpace(permission)) return true;

        return string.Equals(permission, "full", StringComparison.OrdinalIgnoreCase)
            || string.Equals(permission, "write", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveMetadataValue(SkillInvocationContext context, string key)
        => context.Metadata is not null && context.Metadata.TryGetValue(key, out var value) ? value : null;

    private static string Resolve(string rootFull, string rel)
    {
        if (Path.IsPathRooted(rel)) throw new InvalidOperationException("absolute paths are not allowed");
        var full = Path.GetFullPath(Path.Combine(rootFull, rel));
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("path escapes sandbox root");
        return full;
    }
}
