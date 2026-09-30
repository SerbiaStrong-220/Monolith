using Content.Client.Damage;
using Content.Shared._Exodus.Genetics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Player;

namespace Content.Client._Exodus.Genetics;

/// <summary>Updates local presentation without removing the carrier's real hands, inventory or anatomy.</summary>
public sealed partial class GeneticFormPresentationSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private DamageVisualsSystem _damageVisuals = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticEffectsRefreshedEvent>(OnChanged);
        SubscribeLocalEvent<GeneticEffectsComponent, LocalPlayerAttachedEvent>(OnChanged);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticEffectsShutdownEvent>(OnChanged);
    }

    public static bool IsAlternateForm(IEntityManager entities, EntityUid? uid)
    {
        return entities.TryGetComponent<GeneticEffectsComponent>(uid, out var effects) &&
               effects.LifeStage < ComponentLifeStage.Stopping && effects.InAlternateForm;
    }

    private void OnChanged<T>(Entity<GeneticEffectsComponent> ent, ref T args)
    {
        _damageVisuals.RefreshGeneticFormVisuals(ent.Owner);
        if (_player.LocalEntity != ent.Owner)
            return;
        _ui.GetUIController<GeneticFormUIController>().RefreshHud();
    }
}
