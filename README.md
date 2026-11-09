# UnVault Launcher

A lightweight Windows launcher for managing Unreal Engine installations, projects, and Fab assets and plugins.

This fork of [UnVault Launcher](https://github.com/skxdoom/UnVaultLauncher) focuses on startup performance using .NET 10 NativeAOT.

## Run

- Windows 10 (1809+) or Windows 11, x64.
- No .NET installation required.
- The installed Microsoft Edge WebView2 Runtime is required for embedded Epic sign-in.

Extract `UnVaultLauncher-win-x64.zip` and run `UnVaultLauncher.exe`. Keep the EXE and all accompanying DLLs in the same folder.

## Build

Requires the .NET 10 SDK and Visual Studio 2022 or later with the **Desktop development with C++** workload.

From a Visual Studio Developer Shell in the repository root:

```powershell
dotnet publish Source/App -p:PublishProfile=win-x64
```

The release archive is written to `UnVaultLauncher-win-x64.zip` in the repository root.

Developer check: [WebView2](Tests/Browser/README.md).
