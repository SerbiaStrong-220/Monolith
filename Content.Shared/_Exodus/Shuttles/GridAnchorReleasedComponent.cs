namespace Content.Shared._Exodus.Shuttles;

/// <summary>
/// Persists an explicit release of a grid's forced anchor, including after FTL and map loading.
/// Station identity components remain available to unrelated systems such as cryosleep.
/// </summary>
[RegisterComponent]
public sealed partial class GridAnchorReleasedComponent : Component;
