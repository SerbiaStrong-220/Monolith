// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.ObjectModel;
using Content.Shared._Exodus.Economy;
using Content.Shared.Atmos;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// A commodity's group and the rule that selected it. Default and gas groups have no matching rule.
/// </summary>
public readonly record struct MarketCommodityClassification(
    ProtoId<MarketCommodityGroupPrototype> Group,
    ProtoId<MarketCommodityRulePrototype>? Rule = null);

/// <summary>
/// Classifies existing market keys without changing their prices, factors or identity.
/// Prototype rules are evaluated only at startup and after relevant prototype reloads.
/// </summary>
public sealed partial class MarketCommodityGroupSystem : EntitySystem
{
    private sealed class InvalidGroupConfigurationException(string message) : InvalidOperationException(message)
    {
    }

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private TagSystem _tags = default!;

    private ProtoId<MarketCommodityGroupPrototype> _defaultGroup;
    private IReadOnlyDictionary<ProtoId<MarketCommodityGroupPrototype>, float> _impactMultipliers =
        new Dictionary<ProtoId<MarketCommodityGroupPrototype>, float>();
    private IReadOnlyDictionary<string, MarketCommodityClassification> _classifications =
        new ReadOnlyDictionary<string, MarketCommodityClassification>(new Dictionary<string, MarketCommodityClassification>());
    private IReadOnlyDictionary<EntProtoId, MarketCommodityClassification> _prototypeClassifications =
        new ReadOnlyDictionary<EntProtoId, MarketCommodityClassification>(new Dictionary<EntProtoId, MarketCommodityClassification>());

    public override void Initialize()
    {
        base.Initialize();
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
        RebuildClassifications();
    }

    public override void Shutdown()
    {
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    /// <summary>
    /// Resolve a commodity key, using the configured default group for an unknown key.
    /// </summary>
    public ProtoId<MarketCommodityGroupPrototype> GetGroup(string marketKey)
    {
        return TryGetClassification(marketKey, out var classification) ? classification.Group : _defaultGroup;
    }

    /// <summary>
    /// Cached trade pressure multiplier, shared by purchases, sales and their previews.
    /// </summary>
    public float GetImpactMultiplier(string marketKey)
    {
        return _impactMultipliers.GetValueOrDefault(GetGroup(marketKey), 1f);
    }

    /// <summary>
    /// Resolve an object's own group. Stack variants share their canonical spawn's group;
    /// a gas container retains its shell's group independently of the gases inside it.
    /// </summary>
    public ProtoId<MarketCommodityGroupPrototype> GetPrototypeGroup(EntProtoId prototype)
    {
        return _prototypeClassifications.TryGetValue(prototype, out var classification)
            ? classification.Group
            : _defaultGroup;
    }

    public bool TryGetClassification(string marketKey, out MarketCommodityClassification classification)
    {
        return _classifications.TryGetValue(marketKey, out classification);
    }

    public IReadOnlyDictionary<string, MarketCommodityClassification> GetClassifications()
    {
        return _classifications;
    }

    public IReadOnlyDictionary<EntProtoId, MarketCommodityClassification> GetPrototypeClassifications()
    {
        return _prototypeClassifications;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<MarketCommodityGroupPrototype>() || args.WasModified<MarketCommodityRulePrototype>() ||
            args.WasModified<EntityPrototype>() || args.WasModified<StackPrototype>() || args.WasModified<TagPrototype>())
        {
            try
            {
                RebuildClassifications();
            }
            catch (InvalidGroupConfigurationException exception)
            {
                Log.Error($"Could not reload market commodity groups; retaining previous classifications: {exception.Message}");
            }
        }
    }

    private void RebuildClassifications()
    {
        var groups = new Dictionary<ProtoId<MarketCommodityGroupPrototype>, float>();
        ProtoId<MarketCommodityGroupPrototype>? defaultGroup = null;
        ProtoId<MarketCommodityGroupPrototype>? gasGroup = null;
        foreach (var group in _prototypes.EnumeratePrototypes<MarketCommodityGroupPrototype>())
        {
            if (!float.IsFinite(group.ImpactMultiplier) || group.ImpactMultiplier < 0f)
                throw new InvalidGroupConfigurationException($"Market commodity group {group.ID} requires a finite, non-negative impact multiplier.");

            groups.Add(group.ID, group.ImpactMultiplier);
            if (group.Default)
            {
                if (defaultGroup != null)
                    throw new InvalidGroupConfigurationException("Market commodity groups must have exactly one default group.");
                defaultGroup = group.ID;
            }
            if (group.Gases)
            {
                if (gasGroup != null)
                    throw new InvalidGroupConfigurationException("Market commodity groups must have exactly one gas group.");
                gasGroup = group.ID;
            }
        }

        if (defaultGroup == null || gasGroup == null)
            throw new InvalidGroupConfigurationException("Market commodity groups require one default group and one gas group.");

        var rules = new List<MarketCommodityRulePrototype>();
        var matchParents = false;
        foreach (var rule in _prototypes.EnumeratePrototypes<MarketCommodityRulePrototype>())
        {
            if (!groups.ContainsKey(rule.Group))
                throw new InvalidGroupConfigurationException($"Market commodity rule {rule.ID} references unknown group {rule.Group}.");
            rules.Add(rule);
            matchParents |= rule.Parents.Count > 0;
        }
        rules.Sort((a, b) =>
        {
            var priority = b.Priority.CompareTo(a.Priority);
            return priority != 0 ? priority : string.Compare(a.ID, b.ID, StringComparison.Ordinal);
        });

        var fallback = new MarketCommodityClassification(defaultGroup.Value);
        var ownClassifications = new Dictionary<EntProtoId, MarketCommodityClassification>();
        var parents = new HashSet<EntProtoId>();
        foreach (var prototype in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            parents.Clear();
            if (matchParents)
            {
                // Abstract bases are absent from EnumerateParents, but remain valid rule selectors.
                foreach (var (parent, _) in _prototypes.EnumerateAllParents<EntityPrototype>(prototype.ID, includeSelf: true))
                    parents.Add(parent);
            }

            prototype.TryGetComponent<TagComponent>(out var tags, _factory);
            prototype.TryGetComponent<StackComponent>(out var stack, _factory);
            var classification = fallback;
            foreach (var rule in rules)
            {
                if (!Matches(rule, prototype, stack, tags, parents))
                    continue;

                classification = new MarketCommodityClassification(rule.Group, rule.ID);
                break;
            }
            ownClassifications.Add(prototype.ID, classification);
        }

        var classifications = new Dictionary<string, MarketCommodityClassification>(StringComparer.Ordinal);
        var stackClassifications = new Dictionary<ProtoId<StackPrototype>, MarketCommodityClassification>();
        foreach (var stack in _prototypes.EnumeratePrototypes<StackPrototype>())
        {
            var classification = ownClassifications.GetValueOrDefault(stack.Spawn, fallback);
            stackClassifications.Add(stack.ID, classification);
            classifications.Add(DynamicMarketSystem.StackKey(stack.ID), classification);
        }

        var prototypeClassifications = new Dictionary<EntProtoId, MarketCommodityClassification>();
        foreach (var prototype in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            var classification = ownClassifications[prototype.ID];
            if (prototype.TryGetComponent<StackComponent>(out var stack, _factory))
                classification = stackClassifications.GetValueOrDefault(stack.StackTypeId, fallback);

            prototypeClassifications.Add(prototype.ID, classification);
            classifications.Add(DynamicMarketSystem.ProtoKey(prototype.ID), classification);
        }

        var gasClassification = new MarketCommodityClassification(gasGroup.Value);
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
            classifications.Add(DynamicMarketSystem.GasKey(i), gasClassification);

        // Publish complete snapshots together; a failed rebuild leaves the previous caches available.
        _defaultGroup = defaultGroup.Value;
        _impactMultipliers = new ReadOnlyDictionary<ProtoId<MarketCommodityGroupPrototype>, float>(groups);
        _classifications = new ReadOnlyDictionary<string, MarketCommodityClassification>(classifications);
        _prototypeClassifications = new ReadOnlyDictionary<EntProtoId, MarketCommodityClassification>(prototypeClassifications);
    }

    private bool Matches(
        MarketCommodityRulePrototype rule,
        EntityPrototype prototype,
        StackComponent? stack,
        TagComponent? tags,
        HashSet<EntProtoId> parents)
    {
        foreach (var component in rule.ExcludeComponents)
        {
            if (prototype.Components.ContainsKey(component))
                return false;
        }

        if (rule.Prototypes.Contains(prototype.ID) || stack != null && rule.StackTypes.Contains(stack.StackTypeId))
            return true;

        foreach (var component in rule.Components)
        {
            if (prototype.Components.ContainsKey(component))
                return true;
        }

        foreach (var parent in rule.Parents)
        {
            if (parents.Contains(parent))
                return true;
        }

        if (tags != null)
        {
            foreach (var tag in rule.Tags)
            {
                if (_tags.HasTag(tags, tag))
                    return true;
            }
        }

        return false;
    }
}
