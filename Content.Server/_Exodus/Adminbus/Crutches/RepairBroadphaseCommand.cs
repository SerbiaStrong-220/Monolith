using System.Reflection;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Exodus.Adminbus.Crutches;

[AdminCommand(AdminFlags.Admin)]
public sealed class RepairBroadphaseCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "repairbroadphase";
    public string Description => "Repairs broadphase of specified entity if it was broken.";
    public string Help => "Usage: repairbroadphase <EntityUid>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEntity))
        {
            shell.WriteError(Loc.GetString("shell-entity-uid-must-be-number"));
            return;
        }

        var uid = _entManager.GetEntity(netEntity);
        var lookupSys = _entManager.System<EntityLookupSystem>();

        var xform = _entManager.GetComponent<TransformComponent>(uid);
        var fieldInfo = typeof(TransformComponent).GetField("Broadphase", BindingFlags.Instance | BindingFlags.NonPublic)!;
        fieldInfo.SetValue(xform, null);
        lookupSys.FindAndAddToEntityTree(uid, true, xform);
    }
}
