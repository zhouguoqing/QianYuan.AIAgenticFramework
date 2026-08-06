using System.Text;
using QianYuan.Core.Abstractions;

namespace QianYuan.Kernel.ReAct;

/// <summary>
/// Dependency-free token estimator. CJK characters are counted close to one token
/// each; Latin runs are estimated as one token per four characters.
/// </summary>
public sealed class HeuristicTokenCounter : ITokenCounter
{
    public int CountText(string? text, string? model = null)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var tokens = 0;
        var latinRun = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsAsciiWordLike(rune.Value))
            {
                latinRun++;
                continue;
            }

            tokens += FlushLatin(latinRun);
            latinRun = 0;

            if (char.IsWhiteSpace((char)Math.Min(rune.Value, char.MaxValue))) continue;
            tokens += rune.Value <= 0x7f ? 1 : 1;
        }

        tokens += FlushLatin(latinRun);
        return Math.Max(1, tokens);
    }

    private static bool IsAsciiWordLike(int value)
        => value is >= 0x30 and <= 0x39
            or >= 0x41 and <= 0x5a
            or >= 0x61 and <= 0x7a
            or 0x5f;

    private static int FlushLatin(int count) => count <= 0 ? 0 : Math.Max(1, (count + 3) / 4);
}
