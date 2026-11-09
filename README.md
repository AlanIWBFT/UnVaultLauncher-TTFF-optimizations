# UnVault Launcher

A lightweight Windows launcher for managing Unreal Engine installations, projects, and Fab assets and plugins.

This fork of [UnVault Launcher](https://github.com/skxdoom/UnVaultLauncher) focuses on startup performance with .NET 10 NativeAOT. Skia, HarfBuzz, ANGLE and the WebView2 Loader are statically linked into a single executable.

## Run

- Windows 10 (1809+) or Windows 11, x64.
- No .NET installation required.
- The installed Microsoft Edge WebView2 Runtime is required for embedded Epic sign-in.

Run `UnVaultLauncher.exe`; no accompanying application DLLs are needed.

## Build

Requires:

- .NET 10 SDK.
- Visual Studio 2022 or later with the **Desktop development with C++** workload.
- PowerShell 7.
- LLVM, including `llvm-ar` and `lld-link`.

Install LLVM under `%ProgramFiles%\LLVM`, or make its tools available on `PATH`.

From a Visual Studio Developer Shell in the repository root:

```powershell
.\build_release_app.bat
```

The executable is written to `UnVaultLauncher.exe` in the repository root.

Developer check: [WebView2](Tests/Browser/README.md).
