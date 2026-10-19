# UnVault Launcher

A streamlined and lightweight launcher for installing and managing Unreal Engine, your projects, and Fab assets and plugins.

- **Engines**: install, modify, verify and repair. Untick platforms, debug symbols or templates to save disk space.
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

Get `UnVaultLauncher-win-x64.zip` from [Releases](https://github.com/skxdoom/UnVaultLauncher/releases/latest). Extract it and run `UnVaultLauncher.exe`, keeping the DLLs beside it. No .NET installation is needed. Requires 64-bit Windows 10 (1809 or later) or Windows 11, with the WebView2 Runtime for embedded sign-in.

## Building

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Visual Studio 2022 or later with the **Desktop development with C++** workload and Windows SDK, for Native AOT compilation.

Git and GitHub CLI (`gh`) are also needed to build the patched Windows rendering backend.

```
dotnet Build/Avalonia/BuildPatchedAvalonia.cs
dotnet publish Source/App -p:PublishProfile=win-x64
```

The first command checks out pinned Avalonia 12.1.3 sources under `Bin/dependencies`, applies `Build/Avalonia/deferred-d3d11.patch`, and builds a local `Avalonia.Win32` package in `Bin/packages`.
Only this backend is replaced; other Avalonia packages stay at official 12.1.3. Re-run it after changing the patch, and bump the patched package version in both the build helper and app project to avoid NuGet cache reuse.
The app starts rendering on WARP, prepares the hardware device in the background, and switches after its first frame. Set `UNVAULT_RENDERING=hardware` for synchronous hardware startup or `UNVAULT_RENDERING=warp` for fixed WARP rendering when comparing startup behavior.
`UNVAULT_COMPARE=1` enables separate comparison instances, centered side-by-side placement, and a continuous motion probe driven by a shared system clock. The probe deliberately adds redraw activity for visual recording; it is not an FPS measurement.

For native-backend smoke checks on Windows, build `Build/Avalonia/SmokeTest.cs` with `dotnet build Build/Avalonia/SmokeTest.cs -c Release`,
then run `dotnet run --file Build/Avalonia/SmokeTest.cs -c Release --no-build -- switch`. Run it again with `hardware-failure` and `close-early` instead of `switch` to check fallback and early shutdown. Run these GPU checks serially.

The main window stays DWM-cloaked until its initial rendering has crossed the native composition submission boundary, then appears without a reveal transition.
To check this gate, build `Build/Avalonia/FirstFrameSmoke.cs` with `dotnet build Build/Avalonia/FirstFrameSmoke.cs -c Release`, then run
`dotnet run --file Build/Avalonia/FirstFrameSmoke.cs -c Release --no-build -- reveal` and repeat with `close-early`.
The reveal check deliberately delays the render-thread draw and verifies the native cloak flag during that delay and after a second show.

This puts `UnVaultLauncher-win-x64.zip` in the repository root. The unpacked app is in `Bin/publish/UnVault.App/native_win-x64/`. Native libraries are distributed alongside the executable instead of being extracted at startup.

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
