lathe-menu-title = Меню станка
lathe-menu-queue = Очередь
lathe-menu-server-list = Список серверов
lathe-menu-sync = Синхр.
lathe-menu-search-designs = Поиск проектов
lathe-menu-category-all = Всё
lathe-menu-search-filter = Фильтр
lathe-menu-amount = Кол-во:
lathe-menu-recipe-count =
    { $count ->
        [1] { $count } Рецепт
        [few] { $count } Рецепта
       *[other] { $count } Рецептов
    }
lathe-menu-reagent-slot-examine = Сбоку имеется отверстие для мензурки.
lathe-reagent-dispense-no-container = Жидкость выливается из { $name } на пол!
lathe-menu-result-reagent-display = { $reagent } ({ $amount } ед.)
# Exodus-begin - Correctly inflect material units in lathe and material storage interfaces.
lathe-menu-material-display = { CAPITALIZE($material) }: { $amount }
lathe-menu-tooltip-display = { CAPITALIZE($material) }: { $amount }
lathe-menu-description-display = [italic]{ $description }[/italic]
lathe-menu-material-amount = { NATURALFIXED($amount, 2) } { $unit }
lathe-menu-material-amount-missing = { CAPITALIZE($material) }: { NATURALFIXED($amount, 2) } { $unit } ([color=red]нужно добавить: { NATURALFIXED($missingAmount, 2) } { $missingUnit }[/color])
# Exodus-end
lathe-menu-no-materials-message = Материалы не загружены
lathe-menu-silo-linked-message = Хранилище связано
lathe-menu-fabricating-message = Производится...
lathe-menu-materials-title = Материалы
lathe-menu-queue-title = Очередь производства
lathe-menu-delete-fabricating-tooltip = Отменить производство текущего предмета
lathe-menu-delete-item-tooltip = Отменить производство этой партии.
lathe-menu-move-up-tooltip = Перенести эту партию вперёд в очереди.
lathe-menu-move-down-tooltip = Перенести эту партию назад в очереди.
lathe-menu-item-single = { $index }. { $name }
lathe-menu-item-batch = { $index }. { $name } ({ $printed }/{ $total })
lathe-menu-skip = Skip If Insufficient
lathe-menu-loop = Loop
