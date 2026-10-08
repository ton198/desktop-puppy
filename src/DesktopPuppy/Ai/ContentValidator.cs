using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopPuppy.Ai;

public sealed class ContentValidator
{
    public static readonly string[] Styles = ["tease", "deadpan", "absurd", "mock-serious", "wordplay", "dramatic"];
    private static readonly string[] Banned = ["作为AI", "作为 AI", "人工智能", "语言模型", "您好", "很高兴为您", "有什么可以帮", "根据您提供", "希望这能帮助"];
    public bool IsValid(ContentCandidate candidate, IReadOnlyList<ContentCandidate> pool, IReadOnlyList<DisplayedContent> recent)
    {
        var text = candidate.Text;
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || !Styles.Contains(candidate.Style)) return false;
        var count = StringInfo.ParseCombiningCharacters(text).Length;
        if (count is < 10 or > 80 || text.Any(char.IsControl) || text.IndexOfAny(['\n', '\r', '*', '`', '#', '[', ']', '<', '>', '_']) >= 0) return false;
        if (Banned.Any(b => text.Contains(b, StringComparison.OrdinalIgnoreCase)) || Regex.IsMatch(text, @"[!！?？。.,，…]{3,}")) return false;
        // Emoji/symbol clusters: conservative limit of one; variation selectors don't count twice.
        if (text.EnumerateRunes().Count(r => Rune.GetUnicodeCategory(r) == UnicodeCategory.OtherSymbol) > 1) return false;
        if (recent.Count > 0 && recent[0].Candidate.Style == candidate.Style) return false;
        return !pool.Any(c => Similarity(c.Text, text) >= .75) && !recent.Any(c => Similarity(c.Candidate.Text, text) >= .75);
    }
    public static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC).EnumerateRunes().Where(Rune.IsLetterOrDigit).Select(r => r.ToString())).ToLowerInvariant();
    public static double Similarity(string first, string second)
    {
        first = Normalize(first); second = Normalize(second);
        if (first == second) return 1;
        static HashSet<string> Grams(string text) { var runes = text.EnumerateRunes().Select(r => r.ToString()).ToArray(); return Enumerable.Range(0, Math.Max(0, runes.Length - 1)).Select(i => runes[i] + runes[i + 1]).ToHashSet(); }
        var a = Grams(first); var b = Grams(second); return a.Count + b.Count == 0 ? 0 : 2d * a.Intersect(b).Count() / (a.Count + b.Count);
    }
    public static ContentCandidate TieBreak(AiScene scene, IReadOnlyList<ContentCandidate> candidates) => candidates
        .OrderBy(c => scene.Recent.Count(h => h.Candidate.Text == c.Text))
        .ThenByDescending(c => ContentValidator.Similarity(c.Text, scene.Application + scene.Title))
        .ThenBy(c => scene.Recent.Count == 0 ? 0 : scene.Recent.Max(h => Similarity(c.Text, h.Candidate.Text)))
        .ThenBy(c => scene.Recent.Take(3).Count(h => h.Candidate.Style == c.Style))
        .ThenBy(c => StringInfo.ParseCombiningCharacters(c.Text).Length).ThenBy(c => c.Id, StringComparer.Ordinal).First();
}
