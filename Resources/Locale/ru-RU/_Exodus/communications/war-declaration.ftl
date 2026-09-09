# Попарные объявления войны между фракциями
tsf-comms-computer-circuitboard-name = плата консоли связи КВП ТСФ
tsf-comms-computer-circuitboard-description = Компьютерная плата для консоли связи КВП ТСФ.

war-declaration-console-title = Межфракционные отношения
war-declaration-console-title-pending = Межфракционные отношения — мир: {$count}
war-declaration-console-available = Объявление войны доступно.
war-declaration-console-available-in = Объявление войны будет доступно через {$time}.
war-declaration-console-status-cold = Отношения с фракцией «{$target}»: ХОЛОДНАЯ ВОЙНА
war-declaration-console-status-declared = {$source} → {$target}: ПОЛНОМАСШТАБНАЯ ВОЙНА
war-declaration-console-button = Объявить войну фракции «{$target}»
war-declaration-console-confirm = Объявить фракции «{$target}» полномасштабную войну? Мир потребует согласия обеих сторон.

war-declaration-no-access = У вас нет права вести переговоры и объявлять войну с этой консоли.
war-declaration-too-early = Войну ещё нельзя объявить. Осталось: {$time}.
war-declaration-already-active = Эти фракции уже находятся в состоянии войны.
war-declaration-invalid-target = Эта фракция недоступна для дипломатических действий с этой консоли.
war-declaration-failed = Не удалось обработать объявление войны.
war-declaration-round-not-running = Войну можно объявить только во время активного раунда.
war-declaration-success = Фракция «{$declarer}» объявила войну фракции «{$target}».
war-declaration-post-war-cooldown = Повторное объявление войны этой фракции доступно через {$time}.

war-peace-offer-button = Предложить мир
war-peace-accept-button = Принять предложение мира
war-peace-accept-confirm = Заключить мир с фракцией «{$target}»?
war-peace-withdraw-button = Отозвать предложение мира
war-peace-status-outgoing = Предложение мира отправлено фракции «{$target}».
war-peace-status-incoming = Фракция «{$target}» предлагает мир.
war-peace-offer-sent = Предложение мира отправлено. До его принятия война продолжается.
war-peace-offer-accepted = Предложение принято. Война завершена.
war-peace-offer-withdrawn = Предложение мира отозвано. Война продолжается.
war-peace-round-not-running = Переговоры доступны только во время активного раунда.
war-peace-not-at-war = Эти фракции не находятся в состоянии войны.
war-peace-already-pending = Между этими фракциями уже есть предложение мира.
war-peace-offer-cooldown = Новое предложение мира можно отправить через {$time}.
war-peace-offer-unavailable = Это предложение мира уже недействительно.
war-peace-not-offer-sender = Отозвать предложение может только отправившая его сторона.
war-peace-not-offer-recipient = Принять предложение может только вторая сторона.
war-peace-failed = Не удалось обработать предложение мира.
war-peace-agreed-announcement = Фракции «{$first}» и «{$second}» по взаимному согласию заключили мир. Полномасштабная война между ними завершена.

war-declaration-announcement-sender = Дипломатический мониторинг сектора
war-declaration-announcement = ВНИМАНИЕ! Фракция «{$declarer}» объявила полномасштабную войну фракции «{$target}». Гражданскому населению рекомендуется держаться подальше от их военных объектов до окончания конфликта.
war-declaration-ended-announcement = По решению администрации сектора полномасштабная война, объявленная фракцией «{$declarer}» фракции «{$target}», завершена.
war-declaration-cleared-all-announcement = По решению администрации сектора все активные войны фракций завершены.

war-declaration-pda-entry = [color=crimson]{$declarer} → {$target}[/color]

cmd-setwarlevel-hint-2 = [объявившая_фракция]
cmd-setwarlevel-hint-3 = [фракция_цель]
cmd-setwarlevel-invalid-faction = Неизвестная фракция войны: {$faction}.
cmd-setwarlevel-same-faction = Фракция не может объявить войну самой себе.
cmd-setwarlevel-already-active = Эта пара фракций уже находится в состоянии войны.
cmd-setwarlevel-not-active = Эта пара фракций не находится в состоянии войны.
cmd-setwarlevel-state-unavailable = Секторное состояние войн недоступно.
cmd-setwarlevel-failed = Не удалось изменить состояние войны фракций.
cmd-setwarlevel-success-declared = Фракция «{$declarer}» объявила войну фракции «{$target}».
cmd-setwarlevel-success-ended = Война, объявленная фракцией «{$declarer}» фракции «{$target}», завершена.
