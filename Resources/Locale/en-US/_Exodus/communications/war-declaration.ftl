# Pairwise faction war declarations
tsf-comms-computer-circuitboard-name = TSFMC communications computer board
tsf-comms-computer-circuitboard-description = A computer printed circuit board for a TSFMC communications console.

war-declaration-console-title = Interfactional relations
war-declaration-console-title-pending = Interfactional relations — proposals: {$count}
war-declaration-console-available = Declarations of war are available.
war-declaration-console-status-cold = Relations with {$target}: NEUTRAL
war-declaration-console-status-declared = {$source} → {$target}: WAR
war-declaration-console-button = Declare war on {$target}
war-declaration-console-confirm = Declare war on {$target}? Peace will require agreement from both sides.

war-declaration-no-access = You are not authorized to manage diplomacy or the faction code from this console.
war-declaration-code-restricted = The starting faction code prohibits war declarations. Wait for the next code to take effect.
war-declaration-already-active = These factions are already at war.
war-declaration-invalid-target = This faction cannot be selected for diplomacy from this console.
war-declaration-failed = The declaration of war could not be processed.
war-declaration-round-not-running = War may only be declared during an active round.
war-declaration-success = {$declarer} has declared war on {$target}.
war-declaration-post-war-cooldown = War against this faction can be declared again in {$time}.
war-declaration-alliance-break-cooldown = After the alliance was broken, war can be declared in {$time}.
war-declaration-already-allied = These factions are allied. Break the alliance first.
war-declaration-console-button-locked = War on {$target}: {$time}
war-declaration-console-button-allied = Break the alliance with {$target} first
war-declaration-console-status-allied = Relations with {$target}: ALLIANCE
war-declaration-alliance-break-lock = War on {$target} after the broken alliance: {$time}

war-alliance-offer-button = Offer alliance
war-alliance-accept-button = Accept alliance
war-alliance-accept-confirm = Form an alliance with {$target}? Both sides will hear each other's main radio channels. Command channels will remain separate.
war-alliance-withdraw-button = Withdraw alliance offer
war-alliance-break-button = Break alliance
war-alliance-break-confirm = Break the alliance with {$target}? Sharing of main radio channels will stop immediately. War cannot be declared for {$minutes} min.
war-alliance-status-outgoing = An alliance has been offered to {$target}.
war-alliance-status-incoming = {$target} is offering an alliance.
war-alliance-offer-sent = Alliance offer sent. It takes effect when the other side accepts.
war-alliance-offer-accepted = The alliance is in effect.
war-alliance-offer-withdrawn = Alliance offer withdrawn.
war-alliance-broken = Alliance broken. War against this faction is temporarily unavailable.
war-alliance-round-not-running = An alliance can only be offered during an active round.
war-alliance-at-war = An alliance cannot be offered to a faction you are at war with.
war-alliance-already-allied = An alliance with this faction is already in effect.
war-alliance-already-pending = There is already an alliance offer between these factions.
war-alliance-offer-cooldown = Another alliance offer can be sent in {$time}.
war-alliance-offer-unavailable = This alliance offer is no longer valid.
war-alliance-not-offer-sender = Only the offering faction may withdraw this proposal.
war-alliance-not-offer-recipient = Only the other faction may accept this proposal.
war-alliance-not-allied = There is no alliance with this faction.
war-alliance-failed = Failed to process the alliance offer.
war-alliance-formed-announcement = The factions "{$first}" and "{$second}" have formed an alliance. Both sides can now hear each other's main radio channels; command channels remain separate.
war-alliance-broken-announcement = "{$first}" has broken its alliance with "{$second}". Sharing of their main radio channels has stopped. They cannot declare war on each other for {$minutes} minutes.

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
war-peace-agreed-announcement = The factions “{$first}” and “{$second}” have concluded peace by mutual agreement. Their war has ended. Neither side may declare war on the other for {$minutes} minutes.

war-declaration-announcement-sender = Sector Diplomatic Monitoring
war-declaration-announcement = ATTENTION! The faction “{$declarer}” has declared war on the faction “{$target}”. Combat remains subject to the current faction code. Civilians are advised to stay away from their military installations until the conflict ends.
war-declaration-ended-announcement = By decision of sector administration, the war declared by “{$declarer}” against “{$target}” has ended.
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
