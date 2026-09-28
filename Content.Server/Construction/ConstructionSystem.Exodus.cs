// Exodus: consume one unit when an individual-item stack is used as a crafting ingredient.
using Content.Server._Exodus.Stack;

namespace Content.Server.Construction;

public sealed partial class ConstructionSystem
{
    [Dependency] private StackItemSystem _stackItems = default!;
}
