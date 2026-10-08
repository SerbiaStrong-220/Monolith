using System.Globalization;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.Server._Exodus.SafetyDepositBox;

/// <summary>
/// Reads display metadata from a saved item without deserializing or spawning entities.
/// Prototype defaults are intentionally left to the caller.
/// </summary>
public static class SafetyDepositItemMetadata
{
    public static bool TryRead(string data, out string prototypeId, out string? name, out int? count)
    {
        prototypeId = string.Empty;
        name = null;
        count = null;

        if (string.IsNullOrWhiteSpace(data))
            return false;

        try
        {
            using var reader = new StringReader(data);
            var yaml = new YamlStream();
            yaml.Load(reader);

            if (yaml.Documents.Count != 1 ||
                yaml.Documents[0].RootNode is not YamlMappingNode document ||
                !document.Children.TryGetValue("orphans", out var orphanNode) ||
                orphanNode is not YamlSequenceNode { Children.Count: 1 } orphans ||
                !TryReadId(orphans.Children[0], out var rootId) ||
                !document.Children.TryGetValue("entities", out var entityNode) ||
                entityNode is not YamlSequenceNode groups)
                return false;

            // TrySaveEntity requires a single orphan and no serialized maps or grids.
            // Nullspace entities can be referenced dependencies, so they are never item roots.
            if (!IsEmptySequence(document, "maps") || !IsEmptySequence(document, "grids"))
                return false;

            YamlMappingNode? root = null;
            string? rootPrototype = null;
            var ids = new HashSet<int>();
            foreach (var groupNode in groups.Children)
            {
                if (groupNode is not YamlMappingNode group ||
                    !group.Children.TryGetValue("proto", out var prototypeNode) ||
                    !TryReadText(prototypeNode, out var prototype) ||
                    !group.Children.TryGetValue("entities", out var entriesNode) ||
                    entriesNode is not YamlSequenceNode entries)
                    return false;

                foreach (var entryNode in entries.Children)
                {
                    if (entryNode is not YamlMappingNode entry ||
                        !entry.Children.TryGetValue("uid", out var uidNode) ||
                        !TryReadId(uidNode, out var id) ||
                        !ids.Add(id))
                        return false;

                    if (id != rootId)
                        continue;

                    root = entry;
                    rootPrototype = prototype;
                }
            }

            if (root == null || string.IsNullOrWhiteSpace(rootPrototype) ||
                !TryReadOverrides(root, out var rootName, out var rootCount))
                return false;

            prototypeId = rootPrototype;
            name = rootName;
            count = rootCount;
            return true;
        }
        catch (Exception e) when (e is YamlException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadOverrides(YamlMappingNode root, out string? name, out int? count)
    {
        name = null;
        count = null;

        if (!root.Children.TryGetValue("components", out var node))
            return true;

        if (node is not YamlSequenceNode components)
            return false;

        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var componentNode in components.Children)
        {
            if (componentNode is not YamlMappingNode component ||
                !component.Children.TryGetValue("type", out var typeNode) ||
                !TryReadText(typeNode, out var type) ||
                string.IsNullOrWhiteSpace(type) || !types.Add(type))
                return false;

            switch (type)
            {
                case "MetaData" when component.Children.TryGetValue("name", out var nameNode):
                    if (!TryReadText(nameNode, out var overrideName))
                        return false;

                    name = overrideName;
                    break;
                case "Stack" when component.Children.TryGetValue("count", out var countNode):
                    if (countNode is not YamlScalarNode scalar ||
                        !int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var overrideCount) ||
                        overrideCount < 0)
                        return false;

                    count = overrideCount;
                    break;
            }
        }

        return true;
    }

    private static bool TryReadId(YamlNode node, out int id)
    {
        id = 0;
        return node is YamlScalarNode scalar &&
               int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0;
    }

    private static bool TryReadText(YamlNode node, out string text)
    {
        text = string.Empty;
        if (node is not YamlScalarNode { Value: { } value } scalar ||
            scalar.Style == ScalarStyle.Plain &&
            (value.Length == 0 || value == "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase)))
            return false;

        text = value;
        return true;
    }

    private static bool IsEmptySequence(YamlMappingNode document, string key)
    {
        return !document.Children.TryGetValue(key, out var node) ||
               node is YamlSequenceNode { Children.Count: 0 };
    }
}
