using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Administration;
using Content.Shared.Body.Components;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Exodus.Genetics;

public sealed class GeneticsAdminSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly GeneticsSystem _genetics = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnVerbs);
    }

    public bool CanAdminister(ICommonSession player)
    {
        return _admins.HasAdminFlag(player, AdminFlags.Admin);
    }

    private void OnVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor) || !CanAdminister(actor.PlayerSession) ||
            !HasComp<BodyComponent>(args.Target) || !HasComp<HumanoidAppearanceComponent>(args.Target) ||
            HasComp<GeneticIncompatibleComponent>(args.Target))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("genetics-admin-verb"),
            Message = Loc.GetString("genetics-admin-verb-description"),
            Category = VerbCategory.Tricks,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/vv.svg.192dpi.png")),
            Impact = LogImpact.Low,
            Act = () =>
            {
                if (CanAdminister(actor.PlayerSession) && _genetics.TryGetGenome(args.Target, out _))
                    _eui.OpenEui(new GeneticsAdminEui(this, _genetics, _admins, args.Target), actor.PlayerSession);
            },
        });
    }

    public GeneticsAdminState GetState(ICommonSession player, EntityUid target)
    {
        if (!CanAdminister(player) || !_genetics.TryGetGenome(target, out var genome))
            return new GeneticsAdminState(Loc.GetString("genetics-admin-unavailable"), string.Empty, -1, 0, new());

        var round = _genetics.GetRound();
        var blocks = new List<GeneticBlockInfo>();
        for (var i = 0; i < round.Mutations.Count; i++)
        {
            if (round.Mutations[i] is not { } mutation)
            {
                blocks.Add(new GeneticBlockInfo(genome.Blocks[i], Loc.GetString("genetics-empty-block")));
                continue;
            }
            var prototype = _prototypes.Index(mutation);
            blocks.Add(new GeneticBlockInfo(genome.Blocks[i], Loc.GetString(prototype.Name),
                Loc.GetString(prototype.Description), genome.Active.Contains(mutation)));
        }
        return new GeneticsAdminState(Identity.Name(target, EntityManager), genome.Context, genome.Revision, genome.Stability, blocks);
    }

    public bool TrySetBlock(ICommonSession player, EntityUid target, GeneticsAdminSetBlockMessage message)
    {
        if (!CanAdminister(player) || player.AttachedEntity is not { } actor || TerminatingOrDeleted(actor) ||
            !_genetics.TryGetGenome(target, out var genome) || genome.Context != message.Context ||
            genome.Revision != message.Revision || message.Block < 0 || message.Block >= genome.Blocks.Count ||
            _genetics.GetRound().Mutations[message.Block] == null)
            return false;

        return _genetics.TrySetBlock((target, genome), message.Block, message.Enabled ? GeneticsSystem.MaxBlockValue : 0, actor);
    }
}
