# UnVault Launcher

A streamlined and lightweight launcher for installing and managing Unreal Engine, your projects, and Fab assets and plugins.

- **Engines**: install, update, modify, verify and repair. Untick platforms, debug symbols or templates to save disk space.
- **Projects**: open and sort them into your own categories.
- **Library**: install plugins into engines or projects (with optional recompilation), add asset packs to projects and create projects.
- **Downloads**: pause, resume and retry, with a history.
- **Works alongside the Epic Games Launcher**: uses its engines, plugins, Vault Cache and project folders.

## Screenshots

![Engines](https://dmkarpukhin.com/assets/img/unvault_a_0.7.0.jpg)

![Projects](https://dmkarpukhin.com/assets/img/unvault_b_0.7.0.jpg)

![Library](https://dmkarpukhin.com/assets/img/unvault_c_0.7.0.jpg)

![Downloads](https://dmkarpukhin.com/assets/img/unvault_d_0.7.0.jpg)

## Download

Get `UnVaultLauncher.exe` from [Releases](https://github.com/skxdoom/UnVaultLauncher/releases/latest). It's a single file with nothing to install, for 64-bit Windows 10 or 11.

## Building

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet publish Source/App -p:PublishProfile=win-x64
```

This puts `UnVaultLauncher.exe` in the repository root.

There's also a command-line tool for engines, for scripting and diagnostics. It isn't included in releases; `dotnet publish Source/CLI -p:PublishProfile=win-x64` builds it as a single `UnVaultLauncher-CLI.exe` in the repository root. It can sign in, list the versions you own and the engines installed, install, modify, verify and repair engines, and list your Fab library. Installing Fab content and engine prerequisites is in the app only. Run it with `--help` for the commands.

## Security and privacy

- Uses Epic's unofficial launcher API, as other third-party launchers do.
- You sign in on Epic's own page; the app never sees your password.
- Your session is stored encrypted for your Windows account and sent only to Epic.
- Downloads use HTTPS and are checked against the hashes in Epic's manifests.
- Connects only to Epic, Fab and GitHub (the update check, which Settings can turn off).
- Keeps its own data (sign-in, settings, caches) in `%LOCALAPPDATA%\UnVaultLauncher`.
- No telemetry, no background service, no admin rights. Only an engine's prerequisites installer may ask for them.
- Releases aren't code-signed, so Windows may warn about an unknown publisher.

## Transparency

This is mostly a vibe-coded project. Why? Because I was desperate. The Epic Games Launcher has a lot of issues, and for more than a year they've been directly affecting my work, blocking me from downloading engine versions and the assets and plugins I own, including ones I developed and published myself. I'm tired of trying to fix it. There are other third-party launchers, but they often lack features I need.

## Thanks

Epic's launcher API isn't publicly documented. UnVault Launcher builds on what these projects worked out:

- [Legendary](https://github.com/legendary-gl/legendary): sign-in, the launcher's download API and the manifest format
- [egs-api-rs](https://github.com/AchetaGames/egs-api-rs): the Fab library and its downloads

## Disclaimer

Not affiliated with or endorsed by Epic Games. Unreal Engine, Epic Games and Fab are trademarks of Epic Games, Inc. 
