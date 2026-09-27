materials-mining-slurry = liquid metal
materials-unit-mining-slurry = portions
bulk-mining-refinery-ui-slurry = Liquid metal reserves
bulk-mining-refinery-ui-slurry-amount = {$stored} / {$capacity} portions
bulk-mining-refinery-ui-slurry-unlimited = {$stored} portions · unlimited capacity
bulk-mining-refinery-ui-slurry-hint = Local storage and connected suppliers' reserves. The tank fills automatically through ore ducts.
bulk-mining-refinery-ui-gas = Exhaust gas: {$gas}
bulk-mining-refinery-ui-gas-amount = {$stored} / {$capacity} mol
bulk-mining-refinery-ui-gas-unlimited = {$stored} mol
bulk-mining-refinery-ui-pressure = Exhaust buffer pressure: {$pressure} kPa
bulk-mining-refinery-ui-corrosion = [color=orange]Trapped gas is corroding the refinery. Check the exhaust![/color]
bulk-mining-refinery-ui-critical = [color=red]Explosion risk! Critical gas reserve: {$limit} mol.[/color]
bulk-mining-refinery-ui-port-hint = Examine the refinery (Shift + LMB) to reveal its gas outlet. Armored pipes are required.
stack-bulk-mining-pipe = ore ducts
bulk-mining-refinery-exhaust = Exhaust: {$moles} mol, {$pressure} kPa.
bulk-mining-refinery-exhaust-warning = [color=orange]Exhaust is backing up! Prolonged accumulation corrodes the refinery; overfilling causes an explosion. Check the armored gas pipes and mining exhaust injector.[/color]
bulk-mining-refinery-explosion-limit = [color=red]Exhaust limit: {$moles} mol. Reaching the limit will detonate the refinery![/color]
ent-BulkMiningRefinery = liquid metal refinery
    .desc = Separates ore from liquid metal and produces extremely toxic chlorine trifluoride. Connect an armored gas pipe just beyond the south edge of the body, two tiles from the center, and lead it to a mining exhaust injector. Gas overfilling causes a powerful explosion. Ordinary atmospheric pipes do not fit.
ent-BulkMiningRefineryCircuitboard = liquid metal refinery board
    .desc = A circuit board for a liquid metal refinery. Assembly requires one unit of americium.
ent-BulkMiningExhaust = mining exhaust injector
    .desc = A two-tile exhaust unit. Vents chlorine trifluoride through its north-facing nozzle every 27 seconds. Connect armored gas pipes to the south-facing inlet and point the nozzle into space. Ordinary atmospheric pipes do not fit.
ent-BulkMiningExhaustCircuitboard = mining exhaust injector board
    .desc = A circuit board for an industrial waste gas injector.
bulk-mining-exhaust-enabled = [color=green]Enabled.[/color] Discharges every {$seconds} seconds. Keep the nozzle clear.
bulk-mining-exhaust-disabled = [color=red]Disabled.[/color] Activate to resume venting.
ent-BulkMiningPipe = ore duct
    .desc = Carries liquid metal. Lay under machines on exposed plating; remove with wirecutters.
ent-BulkMiningPipeUncuttable = ore duct
    .desc = Carries liquid metal. Cannot be cut.
    .suffix = uncuttable
ent-BulkMiningPipeStack = ore duct coil
    .desc = Lay on exposed plating to link mining lasers and refineries.
    .suffix = Full
ent-BulkMiningPipeStack10 = ore duct coil
    .desc = Lay on exposed plating to link mining lasers and refineries.
    .suffix = 10
ent-BulkMiningPipeStack1 = ore duct coil
    .desc = Lay on exposed plating to link mining lasers and refineries.
    .suffix = 1
