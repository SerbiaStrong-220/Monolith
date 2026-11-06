med-alert-ui-empty = No registered medical incidents.
med-alert-ui-refresh = Refresh
med-alert-ui-unknown-grid = unknown location
med-alert-ui-notification-on = ♫
med-alert-ui-notification-off = ̶♫̶
med-alert-ui-mute-tooltip = Mute notifications

med-alert-status = { $type ->
    [death] DECEASED
    [critical] CRITICAL
    [revived] REVIVED
   *[other] CRITICAL
}

med-alert-ui-entry-subject = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
}
med-alert-ui-entry-location = {$grid} at {$position}

med-alert-notification-header = MedAlert

med-alert-notification = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
}{ $type ->
    [death] has died at {$grid} {$position}.
    [critical] life signs critical at {$grid} {$position}.
    [revived] has been revived at {$grid} {$position}.
   *[other] life signs critical at {$grid} {$position}.
}
