lathe-menu-title = Lathe Menu
lathe-menu-queue = Queue
lathe-menu-server-list = Server list
lathe-menu-sync = Sync
lathe-menu-search-designs = Search designs
lathe-menu-category-all = All
lathe-menu-search-filter = Filter:
lathe-menu-amount = Amount:
lathe-menu-loop = Loop
lathe-menu-skip = Skip If Insufficient
lathe-menu-reagent-slot-examine = It has a slot for a beaker on the side.
lathe-reagent-dispense-no-container = Liquid pours out of {THE($name)} onto the floor!
lathe-menu-result-reagent-display = {$reagent} ({$amount}u)
lathe-menu-material-display = {$material} ({$amount})
lathe-menu-tooltip-display = {$amount} of {$material}
lathe-menu-description-display = [italic]{$description}[/italic]
# Exodus-begin - Material unit localization now inflects total and missing amounts independently.
lathe-menu-material-amount = { NATURALFIXED($amount, 2) } { $unit }
lathe-menu-material-amount-missing = { NATURALFIXED($amount, 2) } { $unit } of { $material } ([color=red]{ NATURALFIXED($missingAmount, 2) } { $missingUnit } missing[/color])
# Exodus-end
lathe-menu-no-materials-message = No materials loaded.
lathe-menu-silo-linked-message = Silo Linked
lathe-menu-fabricating-message = Fabricating...
lathe-menu-materials-title = Materials
lathe-menu-queue-title = Build Queue

# Exodus: keep entity and reagent tooltips separate from material unit formatting.
lathe-menu-entity-amount-missing = {$amount} of {$material} ([color=red]{$missingAmount} missing[/color])
lathe-menu-reagent-amount-missing = {$amount}u of {$material} ([color=red]{$missingAmount}u missing[/color])
