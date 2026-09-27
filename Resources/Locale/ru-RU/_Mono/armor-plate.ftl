# Exodus: updated armor plate mechanics and Fluent arguments.
armor-plate-break = Ваша бронепластина { $plateName } разрушилась!
armor-plate-examine-with-plate = Установлена [color=yellow]{ $plateName }[/color]. Прочность: [color={ $durabilityColor }]{ $percent }%[/color]
armor-plate-examine-with-plate-simple = Установлена [color=yellow]{ $plateName }[/color].
armor-plate-examine-no-plate = Бронепластина не установлена.
armor-plate-examine-no-storage = Нет отсека для бронепластин.
armor-plate-examinable-verb-text = Характеристики бронепластины
armor-plate-examinable-verb-message = Осмотреть защитные свойства и прочность.
armor-plate-attributes-examine = Эта бронепластина:
armor-plate-initial-durability = Рассчитана на [color=yellow]{ $durability }[/color] условных единиц урона.
armor-plate-item-durability = Прочность: [color={ $durabilityColor }]{ $percent }%[/color]
armor-plate-gait-speed = скорость передвижения
armor-plate-gait-walk = скорость ходьбы
armor-plate-gait-sprint = скорость бега
armor-plate-speed-display =
    { $stringClause ->
        [1] Увеличивает { $gait } на [color=yellow]{ $speedPercent }%[/color].
        [-1] Уменьшает { $gait } на [color=yellow]{ $speedPercent }%[/color].
       *[other] Не изменяет { $gait }.
    }
armor-plate-ratios-display =
    { $stringClause ->
        [1] [color=cyan]Поглощает[/color] [color=yellow]{ $ratioPercent }%[/color] урона типа [color=yellow]{ $dmgType }[/color]
        [-1] [color=fuchsia]Усиливает[/color] урон типа [color=yellow]{ $dmgType }[/color] на [color=yellow]{ $ratioPercent }%[/color]
       *[other] Не изменяет урон типа [color=yellow]{ $dmgType }[/color]
    }
armor-plate-multiplier-display = и теряет прочность в размере [color=yellow]{ $multiplier }%[/color] исходного урона.
armor-plate-multiplier-none = без потери прочности.
armor-plate-stamina-source-absorb = [color=cyan]поглощённого[/color]
armor-plate-stamina-concat = и
armor-plate-stamina-source-amplified = [color=fuchsia]дополнительно усиленного[/color]
armor-plate-stamina-source-raw = [color=red]всего входящего[/color]
armor-plate-stamina-value = Наносит [color=yellow]{ $multiplier }%[/color] от { $sources } урона в виде урона выносливости.
