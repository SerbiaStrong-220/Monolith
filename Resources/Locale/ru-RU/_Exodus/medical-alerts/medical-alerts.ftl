med-alert-ui-empty = Нет зарегистрированных инцидентов.
med-alert-ui-refresh = Обновить
med-alert-ui-unknown-grid = неизвестное место

med-alert-status-dead = МЁРТВ
med-alert-status-critical = КРИТИЧЕСКОЕ
med-alert-status-revived = ВОСКРЕШЁН

med-alert-ui-entry-subject = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
}
med-alert-ui-entry-location = {$grid}, {$position}

med-alert-notification-header = МедАлерт

med-alert-notification-dead = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
} погиб в {$grid} {$position}.
med-alert-notification-critical = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
} в критическом состоянии в {$grid} {$position}.
med-alert-notification-revived = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
} воскрешён, сейчас в крите в {$grid} {$position}.
