using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Eui;

namespace Content.Server._Exodus.Genetics;

public sealed class GeneticsAdminEui(GeneticsAdminSystem system, GeneticsSystem genetics, IAdminManager admins, EntityUid target) : BaseEui
{
    public override void Opened()
    {
        base.Opened();
        admins.OnPermsChanged += OnPermissionsChanged;
        genetics.GenomeUpdated += OnGenomeUpdated;
        StateDirty();
    }

    public override void Closed()
    {
        admins.OnPermsChanged -= OnPermissionsChanged;
        genetics.GenomeUpdated -= OnGenomeUpdated;
        base.Closed();
    }

    public override EuiStateBase GetNewState() => system.GetState(Player, target);

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (IsShutDown)
            return;
        if (!system.CanAdminister(Player))
        {
            Close();
            return;
        }
        if (msg is GeneticsAdminSetBlockMessage toggle)
            system.TrySetBlock(Player, target, toggle);
        if (msg is GeneticsAdminSetBlockMessage or GeneticsAdminRefreshMessage)
            StateDirty();
    }

    private void OnPermissionsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !system.CanAdminister(Player))
            Close();
    }

    private void OnGenomeUpdated(EntityUid uid)
    {
        if (uid == target)
            StateDirty();
    }
}
