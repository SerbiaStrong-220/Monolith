# SS220 chat bans, adapted and extended for Exodus.
cmd-chatban-desc = Prevent a player from sending messages in selected chats.
cmd-chatban-help = chatban <name or user ID> <OOC,LOOC,Dead> <reason> [minutes; 0 = permanent] [None/Minor/Medium/High]
cmd-chatunban-desc = Revoke a chat ban by its ban ID.
cmd-chatunban-help = chatunban <chat ban ID>
chat-ban-hint-player = <name or user ID>
chat-ban-hint-chats = <OOC,LOOC,Dead; separate multiple channels with commas>
chat-ban-hint-reason = <reason>
chat-ban-hint-minutes = [minutes; 0 = permanent]
chat-ban-hint-severity = [severity]
chat-unban-hint-id = <chat ban ID>
chat-ban-channel-ooc = OOC
chat-ban-channel-looc = LOOC
chat-ban-channel-dead = Ghost
chat-ban-panel-chats = Chats
chat-ban-invalid-chats = Select OOC, LOOC, or Dead (Ghost). Separate multiple channels with commas.
chat-ban-invalid-input = Check your Ban permission, player, channels, reason (1–1024 characters), duration (0–5256000 minutes) and severity.
chat-ban-invalid-duration = Invalid duration
chat-ban-loading = Your chat restrictions are loading. Please try again shortly.
chat-ban-client-blocked = Sending to this chat is restricted or your permissions are still loading. Your draft has been kept.
chat-ban-blocked = You cannot send messages to { $chat } until { $expires }. Reason: { $reason }
chat-ban-permanent = permanently
chat-ban-applied = You have been muted in { $chats } until { $expires }. Reason: { $reason }
chat-ban-revoked = Chat ban #{ $id } has been revoked.
chat-ban-success = { $admin } muted { $target } in { $chats } until { $expires }: { $reason } (ban #{ $id }).
chat-unban-success = { $admin } revoked chat ban #{ $id }.
chat-ban-created = Chat ban #{ $id } created.
chat-unban-not-found = This chat ban does not exist, is already revoked, or you do not have Ban permission.
chat-ban-error = The chat ban operation could not be completed. Check the punishment history before retrying; details are in the server log.
chat-ban-saving = Saving chat ban…
chat-ban-note = Chat ban: { $chats }. Reason: { $reason }
chat-ban-revoke = Revoke chat ban
