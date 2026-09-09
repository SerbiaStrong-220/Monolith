# Frontier: station<sector
cmd-setalertlevel-desc = Set current sector alert level. An announcement will come from the grid the player is standing on.
cmd-setalertlevel-help = Usage: setalertlevel <level> [locked]
cmd-setalertlevel-invalid-grid = You must be on grid of station code that you are going to change.
cmd-setalertlevel-invalid-level = Specified alert level does not exist on that grid.

cmd-setalertlevel-hint-1 = <level>
cmd-setalertlevel-hint-2 = [locked]

# Exodus-begin: restore manual war-level control without enabling automatic portstrikes.
cmd-setwarlevel-desc = Declares or ends a pairwise faction war. The one-argument form is retained for legacy administration.
cmd-setwarlevel-help = Usage: setwarlevel <true|false> [declarer] [target]
    With one argument, true declares TSFMC → PDV and false ends all wars.
    With three arguments, changes only the specified directed faction war.
cmd-setwarlevel-hint-1 = <true|false>
# Exodus-end
