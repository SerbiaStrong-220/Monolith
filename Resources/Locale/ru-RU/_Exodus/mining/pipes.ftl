materials-mining-slurry = жидкий металл
materials-unit-mining-slurry = порций
bulk-mining-refinery-ui-slurry = Запас жидкого металла
bulk-mining-refinery-ui-slurry-amount = {$stored} / {$capacity} порций
bulk-mining-refinery-ui-slurry-unlimited = {$stored} порций · вместимость не ограничена
bulk-mining-refinery-ui-slurry-hint = Собственный бак и запасы подключённых источников. Бак автоматически наполняется через рудопровод.
bulk-mining-refinery-ui-gas = Отработанный газ: {$gas}
bulk-mining-refinery-ui-gas-amount = {$stored} / {$capacity} моль
bulk-mining-refinery-ui-gas-unlimited = {$stored} моль
bulk-mining-refinery-ui-pressure = Давление в газовом буфере: {$pressure} кПа
bulk-mining-refinery-ui-corrosion = [color=orange]Газ накапливается и разъедает переработчик. Проверьте отвод![/color]
bulk-mining-refinery-ui-critical = [color=red]Опасность взрыва! Критический запас: {$limit} моль.[/color]
bulk-mining-refinery-ui-port-hint = Осмотрите переработчик (Shift + ЛКМ), чтобы увидеть газовый выход. Нужны бронированные трубы.
stack-bulk-mining-pipe = рудопровод
bulk-mining-refinery-exhaust = Отработанный газ: {$moles} моль, {$pressure} кПа.
bulk-mining-refinery-exhaust-warning = [color=orange]Газ накапливается! Длительный застой разъедает переработчик, переполнение вызывает взрыв. Проверьте бронированные трубы и выбрасыватель в космосе.[/color]
bulk-mining-refinery-explosion-limit = [color=red]Предел накопления газа: {$moles} моль. При достижении предела переработчик взорвётся![/color]
ent-BulkMiningRefinery = переработчик жидкого металла
    .desc = Выделяет руду из жидкого металла и производит крайне токсичный трифторид хлора. Подключите бронированную газовую трубу сразу за южным краем корпуса, в двух тайлах от центра, и соедините её с выбрасывателем. Переполнение газом вызывает сильный взрыв. Обычные атмосферные трубы не подходят.
ent-BulkMiningRefineryCircuitboard = плата переработчика жидкого металла
    .desc = Плата для сборки переработчика жидкого металла. Требуется одна единица америция.
ent-BulkMiningExhaust = выбрасыватель отработанного газа
    .desc = Вытянутый механизм размером 1×2 тайла. Выпускает трифторид хлора через северное сопло каждые 27 секунд. Подключите бронированную газовую трубу к южному входу и направьте сопло в космос. Обычные атмосферные трубы не подходят.
ent-BulkMiningExhaustCircuitboard = плата выбрасывателя отработанного газа
    .desc = Плата для сборки промышленного выбрасывателя отработанного газа.
bulk-mining-exhaust-enabled = [color=green]Включён.[/color] Выброс каждые {$seconds} секунд. Оставьте выход сопла свободным.
bulk-mining-exhaust-disabled = [color=red]Выключен.[/color] Активируйте, чтобы возобновить выброс газа.
ent-BulkMiningPipe = рудопровод
    .desc = Переносит жидкий металл. Прокладывается под машинами на открытом покрытии и снимается кусачками.
ent-BulkMiningPipeUncuttable = рудопровод
    .desc = Переносит жидкий металл. Нельзя разрезать.
    .suffix = неразрезаемый
ent-BulkMiningPipeStack = катушка рудопровода
    .desc = Прокладывается на открытом покрытии для соединения буровых лазеров и переработчиков.
    .suffix = Полная
ent-BulkMiningPipeStack10 = катушка рудопровода
    .desc = Прокладывается на открытом покрытии для соединения буровых лазеров и переработчиков.
    .suffix = 10
ent-BulkMiningPipeStack1 = катушка рудопровода
    .desc = Прокладывается на открытом покрытии для соединения буровых лазеров и переработчиков.
    .suffix = 1
