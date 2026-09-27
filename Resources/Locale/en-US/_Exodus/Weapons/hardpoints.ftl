exodus-hardpoint-examine = Mount class: [color=yellow]{ $class }[/color]. Size: [color=yellow]{ $size }[/color]. Compatible weapons of this size or smaller fire at their normal rate. Larger weapons fire more slowly.
exodus-hardpoint-weapon-examine = Mount requirements: [color=yellow]{ $class }[/color], [color=yellow]{ $size }[/color]. Firing rate from the current mounting: [color=yellow]{ $percent }%[/color]. This affects shots and burst cooldowns, but not ammo handling or energy recharge.
exodus-hardpoint-class =
    { $class ->
        [Ballistic] ballistic
        [Energy] energy
        [Missile] missile
        [Universal] universal
       *[other] unknown
    }
exodus-hardpoint-size =
    { $size ->
        [Superlight] superlight
        [Light] light
        [Medium] medium
        [Heavy] heavy
        [Superheavy] superheavy
       *[other] unknown
    }

ent-BaseHardpoint = hardpoint
    .desc = A fixed mounting point for ship weapons. Compatible weapons retain their normal firing rate; unsuitable mounts reduce it.

ent-HardpointDebugSuperlight = superlight universal hardpoint
    .desc = A debug mount for superlight or smaller weapons of any class. Larger weapons fire more slowly.
    .suffix = Debug, DO NOT MAP

ent-HardpointDebugLight = light universal hardpoint
    .desc = A debug mount for light or smaller weapons of any class. Larger weapons fire more slowly.
    .suffix = Debug, DO NOT MAP

ent-HardpointDebugMedium = medium universal hardpoint
    .desc = A debug mount for medium or smaller weapons of any class. Larger weapons fire more slowly.
    .suffix = Debug, DO NOT MAP

ent-HardpointDebugHeavy = heavy universal hardpoint
    .desc = A debug mount for heavy or smaller weapons of any class. Larger weapons fire more slowly.
    .suffix = Debug, DO NOT MAP

ent-HardpointDebugSuperheavy = superheavy universal hardpoint
    .desc = A debug mount for weapons of any class and size.
    .suffix = Debug, DO NOT MAP

ent-HardpointBallisticSuperlight = superlight ballistic hardpoint
    .desc = A fixed mount for superlight or smaller ballistic weapons. Larger weapons fire more slowly.

ent-HardpointBallisticLight = light ballistic hardpoint
    .desc = A fixed mount for light or smaller ballistic weapons. Larger weapons fire more slowly.

ent-HardpointBallisticMedium = medium ballistic hardpoint
    .desc = A fixed mount for medium or smaller ballistic weapons. Larger weapons fire more slowly.

ent-HardpointBallisticHeavy = heavy ballistic hardpoint
    .desc = A fixed mount for heavy or smaller ballistic weapons. Larger weapons fire more slowly.

ent-HardpointBallisticSuperheavy = superheavy ballistic hardpoint
    .desc = A fixed mount for ballistic weapons of any size.

ent-HardpointEnergySuperlight = superlight energy hardpoint
    .desc = A fixed mount for superlight or smaller energy weapons. Larger weapons fire more slowly.

ent-HardpointEnergyLight = light energy hardpoint
    .desc = A fixed mount for light or smaller energy weapons. Larger weapons fire more slowly.

ent-HardpointEnergyMedium = medium energy hardpoint
    .desc = A fixed mount for medium or smaller energy weapons. Larger weapons fire more slowly.

ent-HardpointEnergyHeavy = heavy energy hardpoint
    .desc = A fixed mount for heavy or smaller energy weapons. Larger weapons fire more slowly.

ent-HardpointEnergySuperheavy = superheavy energy hardpoint
    .desc = A fixed mount for energy weapons of any size.

ent-HardpointMissileSuperlight = superlight missile hardpoint
    .desc = A fixed mount for superlight or smaller missile weapons. Larger weapons fire more slowly.

ent-HardpointMissileLight = light missile hardpoint
    .desc = A fixed mount for light or smaller missile weapons. Larger weapons fire more slowly.

ent-HardpointMissileMedium = medium missile hardpoint
    .desc = A fixed mount for medium or smaller missile weapons. Larger weapons fire more slowly.

ent-HardpointMissileHeavy = heavy missile hardpoint
    .desc = A fixed mount for heavy or smaller missile weapons. Larger weapons fire more slowly.

ent-HardpointMissileSuperheavy = superheavy missile hardpoint
    .desc = A fixed mount for missile weapons of any size.
