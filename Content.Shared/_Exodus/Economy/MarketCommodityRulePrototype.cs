// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Generic;

namespace Content.Shared._Exodus.Economy;

/// <summary>
/// Assigns a group when any selector matches and no excluded component is present.
/// Stack variants inherit the classification of their stack type's canonical spawn.
/// </summary>
[Prototype]
public sealed partial class MarketCommodityRulePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Commodity group assigned by this rule.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<MarketCommodityGroupPrototype> Group { get; private set; }

    /// <summary>
    /// Higher values take precedence. Equal priorities are ordered by rule ID using ordinal comparison.
    /// </summary>
    [DataField]
    public int Priority { get; private set; }

    /// <summary>
    /// Match these exact entity prototypes.
    /// </summary>
    [DataField]
    public HashSet<EntProtoId> Prototypes { get; private set; } = new();

    /// <summary>
    /// Match these prototypes and all descendants, including inheritance through abstract bases.
    /// </summary>
    [DataField]
    public HashSet<EntProtoId> Parents { get; private set; } = new();

    /// <summary>
    /// Match entities with any of these registered components.
    /// </summary>
    [DataField(customTypeSerializer: typeof(CustomHashSetSerializer<string, ComponentNameSerializer>))]
    public HashSet<string> Components { get; private set; } = new();

    /// <summary>
    /// Reject entities with any of these components, even if a selector matches.
    /// </summary>
    [DataField(customTypeSerializer: typeof(CustomHashSetSerializer<string, ComponentNameSerializer>))]
    public HashSet<string> ExcludeComponents { get; private set; } = new();

    /// <summary>
    /// Match entities with any of these tags.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<TagPrototype>> Tags { get; private set; } = new();

    /// <summary>
    /// Match any of these stack types using their canonical spawn's components and tags.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<StackPrototype>> StackTypes { get; private set; } = new();
}
