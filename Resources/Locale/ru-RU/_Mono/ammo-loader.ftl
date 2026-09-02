# SS220: retained existing ammo-loader strings; upstream-only failure and entity keys follow.
# Ammo Loader
ammo-loader-flush-verb = Flush
ammo-loader-eject-verb = Eject contents
ammo-loader-insert-success = Ammunition inserted.
ammo-loader-not-anchored = The loader must be anchored to flush!
ammo-loader-empty = The loader is empty!
ammo-loader-flushed = Ammo flushed!
ammo-loader-no-artillery = No ship artillery linked to this loader.
ammo-loader-transfer-failed = Failed to transfer ammunition to any linked ship artillery.
ammo-loader-insert-fail = Loader is full.
# Artillery Flush Verbs
ammo-loader-flush-to-artillery-with-ammo-and-id = Flush to { $artillery } ({ $ammo }/{ $capacity }) [{ $id }]
ammo-loader-flushed-to-artillery = Ammunition flushed to { $artillery }!
ammo-loader-transfer-failed-to-artillery = Failed to transfer ammunition to { $artillery }!
# Device Link Ports
signal-port-name-ammo-loader-load = Load Ammo
signal-port-description-ammo-loader-load = Transfers loaded ammunition to linked ship artillery.
signal-port-name-space-artillery-load = Receive Ammo
signal-port-description-space-artillery-load = Receives ammunition from a linked loader.

ammo-loader-turret-locked = Боеприпасы этой турели нельзя заменить.
ammo-loader-incompatible-ammo = Эти боеприпасы несовместимы с выбранной турелью.
ammo-loader-load-failed = Не удалось зарядить турель.
ammo-loader-unload-failed = Не удалось выгрузить боеприпасы из турели.

ent-AmmoLoader = загрузчик боеприпасов
    .desc = Пневматическая система подачи боеприпасов производства Erebus HI. Свяжите её с корабельной артиллерией мультитулом для передачи боеприпасов. Вмещает до 30 предметов и поддерживает 8 орудий.

ent-AmmoLoaderSmall = малый загрузчик боеприпасов
    .desc = Пневматическая система подачи боеприпасов производства Erebus HI. Свяжите её с корабельной артиллерией мультитулом для передачи боеприпасов. Вмещает до 15 предметов и поддерживает только 2 орудия, но более живуча.
