<p align="center"><img alt="Space Exodus" height="300" src="https://raw.githubusercontent.com/space-exodus/Monolith/0ddfa161945b7dda8c9cea018b7e72066225fae6/Resources/Textures/_Exodus/Logo/logo.png?raw=true" /><img alt="Monolith" height="50" src="https://raw.githubusercontent.com/Monolith-Station/Monolith/89d435f0d2c54c4b0e6c3b1bf4493c9c908a6ac7/Resources/Textures/_Mono/Logo/logo.png?raw=true" /></p>

"Exodus: Monolith" это репозиторий англоязычного фронтира [Monolith](https://github.com/Monolith-Station/Monolith) который работает на движке [Robust Toolbox](https://github.com/space-wizards/RobustToolbox) от Space Wizards написанном на C#.


Это основной репозиторий проекта "SS220 Exodus: Monolith".

Если вы хотите создавать или размещать контент для "SS220 Exodus: Monolith", вам нужен именно этот репозиторий. Он содержит как RobustToolbox, так и набор контента для разработки нового контента.
## Links

[Discord-сервер SS220 Exodus: Monolith](https://discord.com/invite/ss220) | [Discord-сервер Monolith](https://discord.gg/mxY4h2JuUw) | [Steam](https://store.steampowered.com/app/1255460/Space_Station_14/)


## Участие в разработке

Если Вы желаете помочь в улучшении репозитория, решении проблем или создании нового контента, мы рады принять вклад от любого человека. Заходите в Discord, если хотите помочь. Не бойтесь просить о помощи!

Примечание: чтобы ваш вклад был принят, вы должны согласиться с условиями [нашей лицензии CLA](LICENSES/CLA.txt)

## Сборка

Обратитесь к [руководству Space Wizards](https://docs.spacestation14.com/en/general-development/setup/setting-up-a-development-environment.html) для получения общей информации о настройке среды разработки, но имейте в виду, что наш проект — это не то же самое, и многое может не подходить.
Мы предоставляем несколько скриптов, показанных ниже, чтобы упростить работу.

### Зависимости для сборки

> - Git
> - .NET SDK 10.0


### Windows

> 1. Клонируйте этот репозиторий.
> 2. Запустите `Scripts/bat/updateEngine.bat` в терминале или проводнике, чтобы загрузить движок игры.
> 3. Запустите `Scripts/bat/buildAllDebug.bat` после внесения любых изменений в исходный код (Примечание: для полноценной игры стоит запускать `Scripts/bat/buildAllRelease.bat` или `Scripts/bat/buildAllRelease.bat` для маппинга или теста).
> 4. Запустите `Scripts/bat/runQuickAll.bat` чтобы запустить клиент и сервер.
> 5. Подключитесь к localhost в клиенте и играйте.

### Linux

> 1. Клонируйте этот репозиторий.
> 2. Запустите `Scripts/sh/updateEngine.sh` в терминале или проводнике, чтобы загрузить движок игры.
> 3. Запустите `Scripts/sh/buildAllDebug.sh` после внесения любых изменений в исходный код.
> 4. Запустите `Scripts/sh/runQuickAll.sh` чтобы запустить клиент и сервер.
> 5. Подключитесь к localhost в клиенте и играйте.

### MacOS

> Я не знаю никого, кто использует MacOS для тестирования, но шаги, вероятно, примерно такие же, как для Linux.

## Лицензия

Для получения подробной информации о лицензировании внимательно прочтите файл [LEGAL.md](LEGAL.md)
