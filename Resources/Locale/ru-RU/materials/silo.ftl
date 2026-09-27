ore-silo-ui-title = Хранилище материалов
ore-silo-ui-label-clients = Машины
ore-silo-ui-label-mats = Материалы
ore-silo-ui-itemlist-entry =
    { $linked ->
        [true] { "[Связано] " }
       *[False] { "" }
    } { $name } ({ $beacon }) { $inRange ->
        [true] { "" }
       *[false] (Вне зоны доступа)
    }

ore-silo-client-unlink-verb-text = Отвязать от хранилища материалов
ore-silo-client-unlink-verb-message = Отвязать это устройство от {$silo}.
ore-silo-client-disconnected = Отвязано от {$silo}.
ore-silo-client-examine-connected = Подключено к хранилищу материалов: [color=cyan]{$silo}[/color].
ore-silo-client-examine-not-connected = Не подключено к хранилищу материалов.

