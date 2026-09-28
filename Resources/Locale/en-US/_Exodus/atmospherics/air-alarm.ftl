air-alarm-ui-gases-indicator-no-threshold = { $gas }: [color=gray]{ $amount } mol ({ $percentage }%) — no threshold configured[/color]

air-alarm-ui-window-title = Air alarm
air-alarm-ui-window-alarm-type =
    { $type ->
        [Normal] Normal
        [Warning] Warning
        [Danger] Danger
        [Emagged] Emagged
       *[Invalid] Invalid
    }
air-alarm-ui-thresholds-enabled = Enabled
air-alarm-ui-scrubber-gas-filters-title = Gas filters
air-alarm-ui-scrubber-pump-direction =
    { $dir ->
        [Siphoning] Siphoning
       *[Scrubbing] Scrubbing
    }
