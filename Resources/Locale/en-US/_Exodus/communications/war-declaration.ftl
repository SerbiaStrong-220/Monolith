# Pairwise faction war declarations
tsf-comms-computer-circuitboard-name = TSFMC communications computer board
tsf-comms-computer-circuitboard-description = A computer printed circuit board for a TSFMC communications console.

war-declaration-console-title = Interfactional relations
war-declaration-console-title-pending = Interfactional relations — peace offers: {$count}
war-declaration-console-available = Declarations of war are available.
war-declaration-console-available-in = Declarations of war will be available in {$time}.
war-declaration-console-status-cold = Relations with {$target}: COLD WAR
war-declaration-console-status-declared = {$source} → {$target}: TOTAL WAR
war-declaration-console-button = Declare war on {$target}
war-declaration-console-confirm = Declare total war on {$target}? Peace will require agreement from both sides.

war-declaration-no-access = You are not authorized to negotiate peace or declare war from this console.
war-declaration-too-early = War may not be declared yet. Time remaining: {$time}.
war-declaration-already-active = These factions are already at war.
war-declaration-invalid-target = This faction cannot be selected for diplomacy from this console.
war-declaration-failed = The declaration of war could not be processed.
war-declaration-round-not-running = War may only be declared during an active round.
war-declaration-success = {$declarer} has declared war on {$target}.
war-declaration-post-war-cooldown = War against this faction can be declared again in {$time}.

war-peace-offer-button = Offer peace
war-peace-accept-button = Accept peace offer
war-peace-accept-confirm = Conclude peace with {$target}?
war-peace-withdraw-button = Withdraw peace offer
war-peace-status-outgoing = Peace has been offered to {$target}.
war-peace-status-incoming = {$target} is offering peace.
war-peace-offer-sent = Peace offer sent. The war continues until it is accepted.
war-peace-offer-accepted = Offer accepted. The war has ended.
war-peace-offer-withdrawn = Peace offer withdrawn. The war continues.
war-peace-round-not-running = Negotiations are only available during an active round.
war-peace-not-at-war = These factions are not at war.
war-peace-already-pending = There is already a peace offer between these factions.
war-peace-offer-cooldown = Another peace offer can be sent in {$time}.
war-peace-offer-unavailable = This peace offer is no longer valid.
war-peace-not-offer-sender = Only the offering faction may withdraw this proposal.
war-peace-not-offer-recipient = Only the other faction may accept this proposal.
war-peace-failed = Failed to process the peace offer.
war-peace-agreed-announcement = The factions “{$first}” and “{$second}” have concluded peace by mutual agreement. Their total war has ended. Neither side may declare war on the other for {$minutes} minutes.

war-declaration-announcement-sender = Sector Diplomatic Monitoring
war-declaration-announcement = ATTENTION! The faction “{$declarer}” has declared total war on the faction “{$target}”. Civilians are advised to stay away from their military installations until the conflict ends.
war-declaration-ended-announcement = By decision of sector administration, the total war declared by “{$declarer}” against “{$target}” has ended.
war-declaration-cleared-all-announcement = By decision of sector administration, all active faction wars have ended.

war-declaration-pda-entry = [color=crimson]{$declarer} → {$target}[/color]

cmd-setwarlevel-hint-2 = [declarer]
cmd-setwarlevel-hint-3 = [target]
cmd-setwarlevel-invalid-faction = Unknown war faction: {$faction}.
cmd-setwarlevel-same-faction = A faction cannot declare war on itself.
cmd-setwarlevel-already-active = This faction pair is already at war.
cmd-setwarlevel-not-active = This faction pair is not at war.
cmd-setwarlevel-state-unavailable = The sector war state is unavailable.
cmd-setwarlevel-failed = Failed to change the faction war state.
cmd-setwarlevel-success-declared = {$declarer} has declared war on {$target}.
cmd-setwarlevel-success-ended = The war declared by {$declarer} against {$target} has ended.
