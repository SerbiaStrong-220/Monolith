namespace Content.Shared._Exodus.NPC.Command;

/// <summary>
/// Orders/stances driving NPC squads. Stored on the blackboard under NPCBlackboard.CurrentOrders (for grunts
/// as their order, for commander as its stance) and matched by HasOrdersPrecondition in behaviour trees
/// So we can use theese to build up cool behavioral trees, i hope.
/// </summary>
public enum NpcOrder : byte
{
    /// <summary>Stick with the commander, grunts will fight nearby enemies but stay close. Commander fights/patrols.</summary>
    Follow,

    /// <summary>Charge and push the squad target. Grunts ignore damage while attacking.</summary>
    Attack,

    /// <summary>Disengage and fall back with the commander.</summary>
    Retreat,

    /// <summary>Hold position: the commander stops patrolling and the squad guards the spot.</summary>
    Hold,
}
