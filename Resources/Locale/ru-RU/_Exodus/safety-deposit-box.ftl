# Интерфейс консоли банковских ячеек
safety-deposit-console-title = Консоль банковских ячеек
safety-deposit-console-header = Управление банковскими ячейками
safety-deposit-console-purchase-section = Купить новый сейф
safety-deposit-console-bank-payment-note = Оплата списывается с секторного счёта и накоплений.
safety-deposit-console-purchase-small = Малый (2x2) — ${$cost}
safety-deposit-console-purchase-medium = Средний (2x4) — ${$cost}
safety-deposit-console-purchase-large = Большой (2x6) — ${$cost}
safety-deposit-console-deposit-section = Сдать сейф
safety-deposit-console-box-in-slot = Сейф {$id} установлен
safety-deposit-console-no-box-in-slot = Сейф не установлен
safety-deposit-console-deposit-button = Сдать сейф
safety-deposit-console-withdraw-button = Получить
safety-deposit-console-reclaim-button = Восстановить
safety-deposit-console-owned-boxes = Ваши банковские ячейки:
safety-deposit-console-no-boxes = У вас пока нет банковских ячеек.
safety-deposit-console-box-id = ID сейфа: {$id}... — {$status}
safety-deposit-console-box-id-short = ID сейфа: {$id}...
safety-deposit-console-box-deposited = На хранении
safety-deposit-console-box-not-deposited = Не на хранении
safety-deposit-console-box-in-world = Выдан
safety-deposit-console-box-lost = Утрачен

# Сущности банковской ячейки
safety-deposit-box-boxSlot = Слот сейфа
safety-deposit-console-boxSlot = Слот сейфа

# Осмотр
safety-deposit-stored-examine = [color=gray]На предмете осталась метка о том, что он хранился в банковской ячейке.[/color]
safety-deposit-box-examine-id = [color=cyan]ID сейфа:[/color] {$id}...
safety-deposit-box-examine-owner = [color=yellow]Владелец:[/color] {$owner}
safety-deposit-owner-unknown = Неизвестно

# Результаты операций
safety-deposit-error-invalid-size = Недопустимый размер сейфа.
safety-deposit-error-character = Не удалось загрузить данные персонажа.
safety-deposit-error-bank-account = У этого персонажа нет банковского счёта.
safety-deposit-error-insufficient-funds = Недостаточно средств. Сейф стоит ${$cost}; на секторном счёте ${$bank}, в накоплениях ${$savings}.
safety-deposit-error-transaction = Операция не выполнена. Предметы и оплата сохранены или возвращены.
safety-deposit-error-operation-in-progress = Операция с банковской ячейкой уже выполняется. Подождите.
safety-deposit-error-insert-box = Сначала установите сейф в слот.
safety-deposit-error-invalid-box = Это недействительный сейф банковской ячейки.
safety-deposit-error-not-owner = Этот сейф не принадлежит текущему персонажу.
safety-deposit-error-no-storage = В этом сейфе нет отсека для хранения.
safety-deposit-error-serialize = Один из предметов не удалось сохранить. Сейф не был сдан.
safety-deposit-error-box-not-found = Эта банковская ячейка больше не существует в базе данных.
safety-deposit-error-already-stored = Этот сейф уже находится на хранении.
safety-deposit-error-already-withdrawn = Этот сейф уже выдан в игровой мир.
safety-deposit-error-not-lost = Этот сейф не утрачен, поэтому его нельзя восстановить.

safety-deposit-purchase-success = Сейф банковской ячейки куплен. ID сейфа: {$id}...
safety-deposit-deposit-success = Содержимое сейфа сохранено. Сейф принят на хранение.
safety-deposit-withdraw-success = Сейф банковской ячейки получен.
safety-deposit-reclaim-success = Утраченный сейф восстановлен. Выдана пустая замена.
