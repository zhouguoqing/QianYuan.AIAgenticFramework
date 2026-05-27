using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;

namespace QianYuan.Kernel.Skills;

public sealed class MarkdownSkillDirectoryOptions
{
    public string Path { get; set; } = "";
    public bool Recursive { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string IdPrefix { get; set; } = "skill";
}

public static class MarkdownSkillLoaderExtensions
{
    public static void RegisterMarkdownSkillsFromDirectories(
        this IServiceProvider sp,
        IEnumerable<MarkdownSkillDirectoryOptions> directories)
    {
        var manager = sp.GetRequiredService<ISkillManager>();
        var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("QianYuan.Kernel.Skills.MarkdownSkillLoader");

        foreach (var options in directories.Where(d => d.Enabled && !string.IsNullOrWhiteSpace(d.Path)))
        {
            foreach (var skill in MarkdownSkillLoader.LoadFromDirectory(options.Path, options.Recursive, options.IdPrefix, logger))
            {
                manager.Register(skill);
            }
        }
    }
}

public static class MarkdownSkillLoader
{
    private static readonly char[] ListSeparators = [',', ';'];

    public static IReadOnlyList<MarkdownSkill> LoadFromDirectory(
        string directory,
        bool recursive = true,
        string idPrefix = "skill",
        ILogger? logger = null)
    {
        var fullDirectory = ResolveDirectory(directory);
        if (!Directory.Exists(fullDirectory))
        {
            logger?.LogWarning("Markdown skill directory does not exist: {Directory}", fullDirectory);
            return Array.Empty<MarkdownSkill>();
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(fullDirectory, "Skill.md", option)
            .Concat(Directory.EnumerateFiles(fullDirectory, "SKILL.md", option))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        var skills = new List<MarkdownSkill>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            try
            {
                var skill = LoadFromFile(file, fullDirectory, idPrefix);
                if (!ids.Add(skill.Id))
                {
                    logger?.LogWarning("Skipping duplicate Markdown skill id {SkillId} from {File}", skill.Id, file);
                    continue;
                }
                skills.Add(skill);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                logger?.LogWarning(ex, "Skipping invalid Markdown skill file {File}", file);
            }
        }

        return skills;
    }

    public static MarkdownSkill LoadFromFile(string filePath, string? rootDirectory = null, string idPrefix = "skill")
    {
        var fullPath = Path.GetFullPath(filePath);
        var text = File.ReadAllText(fullPath);
        var parsed = Parse(text);
        var fallbackName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? Path.GetFileNameWithoutExtension(fullPath);

        var name = GetFirst(parsed.Frontmatter, "name", "title") ?? fallbackName;
        var description = GetFirst(parsed.Frontmatter, "description", "summary") ?? FirstNonEmptyMarkdownLine(parsed.Body) ?? name;
        var id = GetFirst(parsed.Frontmatter, "id") ?? BuildId(idPrefix, rootDirectory, fullPath, name);
        var tags = GetList(parsed.Frontmatter, "tags", "keywords", "categories");
        if (tags.Count == 0) tags = BuildFallbackTags(name, description);

        var prompt = BuildPrompt(name, description, parsed.Body, fullPath);
        return new MarkdownSkill(
            NormalizeId(id),
            name.Trim(),
            description.Trim(),
            tags,
            prompt,
            fullPath);
    }

    public static SkillManifest BuildManifest(MarkdownSkill skill)
        => new(skill.Id, skill.Name, skill.Description, skill.Tags, 0, RequiresNetwork: false, RequiresFilesystem: true);

    internal static ParsedMarkdownSkill Parse(string text)
    {
        using var reader = new StringReader(text.Replace("\r\n", "\n", StringComparison.Ordinal));
        var firstLine = reader.ReadLine();
        if (firstLine?.Trim() != "---")
            return new ParsedMarkdownSkill(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase), text.Trim());

        var frontmatter = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        var body = new List<string>();
        var inFrontmatter = true;

        while (reader.ReadLine() is { } line)
        {
            if (inFrontmatter)
            {
                if (line.Trim() == "---")
                {
                    inFrontmatter = false;
                    continue;
                }

                if (line.TrimStart().StartsWith("- ", StringComparison.Ordinal) && currentKey is not null)
                {
                    frontmatter[currentKey].Add(Unquote(line.TrimStart()[2..].Trim()));
                    continue;
                }

                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0)
                {
                    currentKey = line[..separator].Trim();
                    var value = line[(separator + 1)..].Trim();
                    frontmatter[currentKey] = ParseYamlValue(value);
                }
                continue;
            }

            body.Add(line);
        }

        if (inFrontmatter)
            return new ParsedMarkdownSkill(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase), text.Trim());

        return new ParsedMarkdownSkill(frontmatter, string.Join("\n", body).Trim());
    }

    private static string ResolveDirectory(string directory)
        => Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory));

    private static List<string> ParseYamlValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        value = value.Trim();
        if (value is "[]") return new List<string>();

        if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
        {
            return value[1..^1]
                .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Unquote)
                .Where(v => v.Length > 0)
                .ToList();
        }

        return new List<string> { Unquote(value) };
    }

    private static string? GetFirst(Dictionary<string, List<string>> frontmatter, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (frontmatter.TryGetValue(key, out var values))
            {
                var value = values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        return null;
    }

    private static IReadOnlyList<string> GetList(Dictionary<string, List<string>> frontmatter, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (frontmatter.TryGetValue(key, out var values) && values.Count > 0)
            {
                return values
                    .SelectMany(v => v.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Select(Unquote)
                    .Where(v => v.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> BuildFallbackTags(string name, string description)
        => Tokenize($"{name} {description}").Take(8).ToArray();

    private static string BuildId(string idPrefix, string? rootDirectory, string fullPath, string name)
    {
        var prefix = string.IsNullOrWhiteSpace(idPrefix) ? "skill" : idPrefix.Trim();
        var relative = rootDirectory is null
            ? name
            : Path.GetRelativePath(Path.GetFullPath(rootDirectory), Path.GetDirectoryName(fullPath) ?? fullPath);
        return $"{prefix}.{relative}";
    }

    private static string NormalizeId(string raw)
    {
        var tokens = Tokenize(raw.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.'));
        return tokens.Count == 0 ? "skill.unnamed" : string.Join(".", tokens);
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        foreach (var ch in text.ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(ch)) current.Add(ch);
            else Flush();
        }
        Flush();
        return tokens;

        void Flush()
        {
            if (current.Count == 0) return;
            tokens.Add(new string(current.ToArray()));
            current.Clear();
        }
    }

    private static string? FirstNonEmptyMarkdownLine(string body)
    {
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = line.Trim().TrimStart('#').Trim();
            if (trimmed.Length > 0) return trimmed.Length <= 160 ? trimmed : trimmed[..160];
        }
        return null;
    }

    private static string BuildPrompt(string name, string description, string body, string sourcePath)
        => $"Skill: {name}\nDescription: {description}\nSource: {sourcePath}\n\n{body}".Trim();

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1].Trim();
        return value;
    }
}

internal sealed record ParsedMarkdownSkill(Dictionary<string, List<string>> Frontmatter, string Body);