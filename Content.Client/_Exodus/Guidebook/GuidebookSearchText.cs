using System.Text;
using System.Text.RegularExpressions;
using Robust.Shared.Utility;

namespace Content.Client._Exodus.Guidebook;

/// <summary>
/// Extracts searchable article text without constructing UI controls or spawning embedded entities.
/// </summary>
public static class GuidebookSearchText
{
    private static readonly Regex DocumentTags = new(
        @"\\(?<escape>[<>\\\-="" nt])|<!--[\s\S]*?-->|<(?:\\.|""(?:\\.|[^""\\])*""|[^""\\>])*>",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex Caption = new(
        @"\bCaption=""((?:\\.|[^""\\])*)""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex HeadingsAndLists = new(
        @"(?m)^\s*(?:#{1,3}|--?)\s+",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Keeps prose, link labels and explicit captions, excluding comments, prototype IDs and formatting.
    /// Generated card contents remain accessible through the existing article-specific filter.
    /// </summary>
    public static string Extract(string document)
    {
        var text = DocumentTags.Replace(document, match =>
        {
            // Match document escapes in the same pass so escaped angle brackets cannot become tags.
            if (match.Groups["escape"].Success)
            {
                return match.Groups["escape"].Value switch
                {
                    "n" => "\n",
                    "t" => "\t",
                    var character => character,
                };
            }

            if (match.Value.StartsWith("<!--", StringComparison.Ordinal))
                return "\n";

            var caption = Caption.Match(match.Value);
            return caption.Success
                ? "\n" + FormattedMessage.EscapeText(caption.Groups[1].Value) + "\n"
                : "\n";
        });
        text = HeadingsAndLists.Replace(text, "");

        var builder = new StringBuilder();
        foreach (var node in FormattedMessage.FromMarkupPermissive(text))
        {
            if (node.IsPlainText || node.Name == "textlink" && !node.Closing)
                builder.Append(node.Value.StringValue);
        }

        return CollapseWhitespace(builder.ToString());
    }

    public static string Normalize(string text)
    {
        return CollapseWhitespace(text).ToLowerInvariant().Replace('ё', 'е');
    }

    /// <summary>
    /// Ranks normalized matches: exact title, part of a title, then article text. -1 means no match.
    /// </summary>
    public static int GetMatchRank(string title, string text, string query)
    {
        if (query.Length == 0)
            return -1;

        if (title == query)
            return 0;

        if (title.Contains(query, StringComparison.Ordinal))
            return 1;

        return text.Contains(query, StringComparison.Ordinal) ? 2 : -1;
    }

    /// <summary>
    /// Returns a short excerpt around the first body match, or the beginning for title-only matches.
    /// Text must have collapsed whitespace, as returned by <see cref="Extract"/>.
    /// </summary>
    public static string GetSnippet(string text, string query, int length = 160)
    {
        if (text.Length <= length)
            return text;

        var match = Normalize(text).IndexOf(query, StringComparison.Ordinal);
        var start = Math.Max(0, match - 40);
        var end = Math.Min(text.Length, Math.Max(start + length, match + query.Length));

        // Keep complete words at excerpt boundaries when possible.
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;

        return (start > 0 ? "…" : "") + text[start..end].Trim() + (end < text.Length ? "…" : "");
    }

    public static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
                builder.Append(' ');

            builder.Append(character);
            space = false;
        }

        return builder.ToString();
    }
}
