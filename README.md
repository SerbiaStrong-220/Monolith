<p align="center"><img alt="Space Exodus" height="300" src="https://raw.githubusercontent.com/space-exodus/Monolith/0ddfa161945b7dda8c9cea018b7e72066225fae6/Resources/Textures/_Exodus/Logo/logo.png?raw=true" /><img alt="Monolith" height="50" src="https://raw.githubusercontent.com/Monolith-Station/Monolith/89d435f0d2c54c4b0e6c3b1bf4493c9c908a6ac7/Resources/Textures/_Mono/Logo/logo.png?raw=true" /></p>

"Exodus: Monolith" is a fork of [Monolith](https://github.com/Monolith-Station/Monolith) that runs on the [Robust Toolbox](https://github.com/space-wizards/RobustToolbox) engine written in C#.

This is the primary repo for "Exodus: Monolith".

If you want to host or create content for "Exodus: Monolith", this is the repo you need. It contains both RobustToolbox and the content pack for development of new content packs.

## Links

[Discord of Exodus: Monolith](https://discord.gg/yZKf8cQVHC) | [Discord of Monolith](https://discord.gg/mxY4h2JuUw) | [Steam](https://store.steampowered.com/app/1255460/Space_Station_14/)

## Contributing

We are happy to accept contributions from anybody. Get in Discord if you want to help. Don't be afraid to ask for help either!

Note: In order for your contribution to be accepted, you must agree to the terms of [our CLA](LICENSES/CLA.txt)

## Building

Refer to [the Space Wizards' guide](https://docs.spacestation14.com/en/general-development/setup/setting-up-a-development-environment.html) on setting up a development environment for general information, but keep in mind that Einstein Engines is not the same and many things may not apply.
We provide some scripts shown below to make the job easier.

### Build dependencies

> - Git
> - .NET SDK 9.0.101


### Windows

> 1. Clone this repository
> 2. Run `Scripts/bat/updateEngine.bat` in a terminal or in file explorer to download the engine
> 3. Run `Scripts/bat/buildAllDebug.bat` after making any changes to the source
> 4. Run `Scripts/bat/runQuickAll.bat` to launch the client and the server
> 5. Connect to localhost in the client and play

### Linux

> 1. Clone this repository
> 2. Run `Scripts/sh/updateEngine.sh` in a terminal to download the engine
> 3. Run `Scripts/sh/buildAllDebug.sh` after making any changes to the source
> 4. Run `Scripts/sh/runQuickAll.sh` to launch the client and the server
> 5. Connect to localhost in the client and play

### MacOS

> I don't know anybody using MacOS to test this, but it's probably roughly the same steps as Linux

## License

For detailed licensing information, read the file carefully [LEGAL.md](LEGAL.md)
