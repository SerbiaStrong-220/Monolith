// Exodus: шуточные изменения — закреплённые титулы; отключение и откат описаны в README.md.
using System.Collections.Frozen;
using System.IO;
using Content.Shared.Administration;
using Robust.Shared.Player;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.Server._Exodus.Administration;

/// <summary>
/// Account titles that take precedence over rank names and personal admin titles.
/// An immutable snapshot of a physical server file, separate from CVars and uploaded resources.
/// </summary>
public sealed class FixedAdminTitles
{
    public const string FileName = "admin_title_pranks.yml";

    public static FixedAdminTitles Disabled { get; } = new(FrozenDictionary<string, string>.Empty);

    private readonly FrozenDictionary<string, string> _titles;

    private FixedAdminTitles(FrozenDictionary<string, string> titles)
    {
        _titles = titles;
    }

    public static FixedAdminTitles LoadFromFile(string path, ISawmill log)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > 64 * 1024)
                throw new YamlException("The file must not exceed 64 KiB.");

            using var reader = new StreamReader(file);
            var yaml = new YamlStream();
            yaml.Load(reader);
            if (yaml.Documents.Count != 1)
                throw new YamlException("Expected exactly one configuration document.");

            return Parse(yaml.Documents[0].RootNode);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return Disabled;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or YamlException)
        {
            log.Warning("Joke admin titles disabled: could not load {0}: {1}", path, e.Message);
            return Disabled;
        }
    }

    private static FixedAdminTitles Parse(YamlNode root)
    {
        if (root is not YamlMappingNode mapping)
            throw new YamlException("Expected a mapping with enabled and titles fields.");

        bool? enabled = null;
        YamlMappingNode? titles = null;
        foreach (var (key, value) in mapping.Children)
        {
            switch ((key as YamlScalarNode)?.Value)
            {
                case "enabled" when enabled == null && value is YamlScalarNode scalar && bool.TryParse(scalar.Value, out var flag):
                    enabled = flag;
                    break;
                case "titles" when titles == null && value is YamlMappingNode titleMapping:
                    titles = titleMapping;
                    break;
                default:
                    throw new YamlException("Unknown, duplicate or incorrectly typed configuration field.");
            }
        }

        if (enabled == null || titles == null)
            throw new YamlException("Both enabled (true/false) and titles (mapping) are required.");

        if (!enabled.Value)
            return Disabled;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in titles.Children)
        {
            if (!result.TryAdd(ReadText(key), ReadText(value)))
                throw new YamlException("Duplicate account name (case-insensitive).");
        }

        return new FixedAdminTitles(result.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    private static string ReadText(YamlNode node)
    {
        if (node is not YamlScalarNode { Value: { } text } scalar || !IsValidText(text) ||
            scalar.Style == ScalarStyle.Plain && (text == "~" || text.Equals("null", StringComparison.OrdinalIgnoreCase)))
            throw new YamlException("Account names and titles must be non-null, non-empty single-line strings of at most 200 characters without surrounding whitespace.");

        return text;
    }

    private static bool IsValidText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200 || text != text.Trim())
            return false;

        foreach (var character in text)
        {
            if (char.IsControl(character) || character is '\u2028' or '\u2029')
                return false;
        }

        return true;
    }

    public string? GetTitle(ICommonSession session, AdminData? adminData)
    {
        if (adminData == null)
            return null;

        // Session.Name can be changed in-game; the channel keeps the login account name.
        if (session.Channel is { } channel && _titles.TryGetValue(channel.UserName, out var title))
            return title;

        return adminData.Title;
    }
}
