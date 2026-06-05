ent-ShipRepairDeviceBase = SRD
    .desc = A Ship Repair Device that can reconstruct destroyed sections of ships.
ent-ShipRepairDevice = { ent-ShipRepairDeviceBase }
    .desc = A Ship Repair Device that can reconstruct destroyed sections of ships. Holds 300 charges.
ent-ShipRepairDeviceEmpty = { ent-ShipRepairDevice }
    .suffix = Empty
    .desc = { ent-ShipRepairDevice.desc }
ent-ShipRepairDeviceRecharging = { ent-ShipRepairDeviceBase }
    .desc = A Ship Repair Device that can reconstruct destroyed sections of ships. Holds 300 charges and slowly recharges.
    .suffix = Recharging
ent-ShipRepairDeviceAdmin = { ent-ShipRepairDeviceBase }
    .suffix = Admin
    .desc = { ent-ShipRepairDeviceBase.desc }
# Exodus: translation ShipRepairDeviceRedacted has been moved into Resources/Locale/ru-RU/_Exodus/prototypes/entities/objects/tools/repair.ftl
ent-ShipRepairDeviceAmmo = ship repair matter
    .desc = Ammo cartridge for a ship repair device.
