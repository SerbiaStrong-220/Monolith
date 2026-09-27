// Exodus: restore appearance data without passing a detached component to Resolve.
using Robust.Shared.GameObjects.Components.Localization;

namespace Content.Shared.Humanoid;

public abstract partial class SharedHumanoidAppearanceSystem
{
    [Dependency] private readonly GrammarSystem _grammar = default!;

    /// <summary>Copies visual data from a live component or a detached snapshot, preserving the target's anatomy.</summary>
    public void ApplyAppearance(Entity<HumanoidAppearanceComponent?> ent, HumanoidAppearanceComponent snapshot)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;
        ent.Comp.Species = snapshot.Species;
        ent.Comp.SkinColor = snapshot.SkinColor;
        ent.Comp.EyeColor = snapshot.EyeColor;
        ent.Comp.Age = snapshot.Age;
        SetSex(ent, snapshot.Sex, false, ent.Comp);
        ent.Comp.CustomBaseLayers = new(snapshot.CustomBaseLayers);
        ent.Comp.MarkingSet = new(snapshot.MarkingSet);
        SetTTSVoice(ent, snapshot.Voice, ent.Comp);
        ent.Comp.Gender = snapshot.Gender;
        if (TryComp<GrammarComponent>(ent, out var grammar))
            _grammar.SetGender((ent.Owner, grammar), snapshot.Gender);
        Dirty(ent);
    }
}
