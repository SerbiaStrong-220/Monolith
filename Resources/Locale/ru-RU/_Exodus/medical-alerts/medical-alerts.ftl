med-alert-ui-empty = Нет зарегистрированных инцидентов.
med-alert-ui-refresh = Обновить
med-alert-ui-unknown-grid = неизвестное место
med-alert-ui-notification-on = ♫
med-alert-ui-notification-off = ̶♫̶
med-alert-ui-mute-tooltip = Отключить уведомления

med-alert-status = { $type ->
    [death] МЁРТВ
    [critical] КРИТИЧЕСКОЕ
    [revived] РЕАНИМИРОВАН
   *[other] КРИТИЧЕСКОЕ
}

med-alert-ui-entry-subject = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
}
med-alert-ui-entry-location = {$grid}, {$position}

med-alert-notification-header = МедОповещения

med-alert-notification = {$user}{ $specie ->
    [null] { "" }
   *[default] { " "}({ $specie })
}{ $type ->
    [death] погиб на {$grid} {$position}.
    [critical] в критическом состоянии на {$grid} {$position}.
    [revived] реанимирован на {$grid} {$position}.
   *[other] в критическом состоянии на {$grid} {$position}.
}
