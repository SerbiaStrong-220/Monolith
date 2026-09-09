# SS220 chat bans, адаптация и дополнения Exodus.
cmd-chatban-desc = Запретить игроку отправлять сообщения в выбранные чаты.
cmd-chatban-help = chatban <ник или ID игрока> <OOC,LOOC,Dead> <причина> [минуты; 0 = навсегда] [None/Minor/Medium/High]
cmd-chatunban-desc = Снять мут по ID бана чата.
cmd-chatunban-help = chatunban <ID бана чата>
chat-ban-hint-player = <ник или ID игрока>
chat-ban-hint-chats = <OOC,LOOC,Dead; несколько каналов через запятую>
chat-ban-hint-reason = <причина>
chat-ban-hint-minutes = [минуты; 0 = навсегда]
chat-ban-hint-severity = [тяжесть нарушения]
chat-unban-hint-id = <ID бана чата>
chat-ban-channel-ooc = OOC
chat-ban-channel-looc = LOOC
chat-ban-channel-dead = Ghost
chat-ban-panel-chats = Чаты
chat-ban-invalid-chats = Выберите OOC, LOOC или Dead (Ghost). Несколько каналов указываются через запятую.
chat-ban-invalid-input = Проверьте право Ban, игрока, каналы, причину (1–1024 символа), срок (0–5256000 минут) и тяжесть нарушения.
chat-ban-invalid-duration = Некорректный срок
chat-ban-loading = Ограничения чата загружаются. Попробуйте через несколько секунд.
chat-ban-client-blocked = Отправка в этот чат ограничена или права ещё загружаются. Набранный текст сохранён.
chat-ban-blocked = Вам запрещено писать в { $chat }. Срок: { $expires }. Причина: { $reason }
chat-ban-permanent = навсегда
chat-ban-applied = Вам запрещено писать в { $chats }. Срок: { $expires }. Причина: { $reason }
chat-ban-revoked = Мут #{ $id } снят.
chat-ban-success = { $admin } выдал { $target } мут в { $chats }. Срок: { $expires }. Причина: { $reason } (бан #{ $id }).
chat-unban-success = { $admin } снял мут #{ $id }.
chat-ban-created = Мут выдан. ID бана: { $id }.
chat-unban-not-found = Такой бан чата не найден, уже снят или у вас нет права Ban.
chat-ban-error = Не удалось завершить операцию с мутом. Перед повторной попыткой проверьте историю наказаний; подробности — в логе сервера.
chat-ban-saving = Сохранение мута…
chat-ban-note = Мут чатов: { $chats }. Причина: { $reason }
chat-ban-revoke = Снять мут
