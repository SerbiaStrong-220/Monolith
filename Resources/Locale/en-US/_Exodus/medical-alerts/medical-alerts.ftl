med-alert-ui-empty = No registered medical incidents.
med-alert-ui-refresh = Refresh
med-alert-ui-unknown-grid = unknown location

med-alert-status-dead = DECEASED
med-alert-status-critical = CRITICAL
med-alert-status-revived = REVIVED

med-alert-ui-entry-subject = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
}
med-alert-ui-entry-location = {$grid} at {$position}

med-alert-notification-header = MedAlert

med-alert-notification-dead = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
} has died at {$grid} {$position}.
med-alert-notification-critical = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
} life signs critical at {$grid} {$position}.
med-alert-notification-revived = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
} revived, now critical at {$grid} {$position}.
