using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace QianYuan.Api.Services;

public static class KnowledgeSearch
{
    public static int ScoreByKeyword(string q, KnowledgeDocument d)
    {
        var tokens = Tokenize(q);
        var tags = d.Tags is null ? string.Empty : string.Join(' ', d.Tags);
        var content = string.Join(' ', new[] { d.Title ?? string.Empty, d.Content ?? string.Empty, tags }).ToLowerInvariant();
        return tokens.Sum(t => Regex.Matches(content, Regex.Escape(t)).Count);
    }

    public static IReadOnlyList<string> Tokenize(string text)
        => Regex.Split(text ?? string.Empty, "\\W+")
            .Where(x => x.Length > 1)
            .Select(x => x.ToLowerInvariant())
            .ToArray();
}
