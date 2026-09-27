ore-silo-ui-title = Material Silo
ore-silo-ui-label-clients = Machines
ore-silo-ui-label-mats = Materials
ore-silo-ui-itemlist-entry = {$linked ->
[true] {"[Linked] "}
*[False] {""}
} {$name} ({$beacon}) {$inRange ->
[true] {""}
*[false] (Out of Range)
}


ore-silo-client-unlink-verb-text = Disconnect from material silo
ore-silo-client-unlink-verb-message = Disconnect this machine from {$silo}.
ore-silo-client-disconnected = Disconnected from {$silo}.
ore-silo-client-examine-connected = Connected to material silo: [color=cyan]{$silo}[/color].
ore-silo-client-examine-not-connected = Not connected to a material silo.

