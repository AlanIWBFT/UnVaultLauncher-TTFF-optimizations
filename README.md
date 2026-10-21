# UnVault Launcher - Time-to-First-Frame optimizations

- NativeAOT compatible (single EXE with statically linked Skia, HarfBuzz, ANGLE and WebView2 Loader; no SFX on startup). The initial NativeAOT conversion saved about 200 ms versus the SFX version.
- A trick I stole from @thomasklemenc 's TaskSlinger (https://taskslinger.net/) : create a WARP device first and show the first frame, while creating the hardware device in the background. When ready, switch to the hardware one. This requires a patched Avalonia build (the build scripts are modified to do this automatically). ~300ms -> 40-50ms

- Other minor tweaks.

## Build

Requires .NET 10, Visual Studio 2022 or later with the Desktop development with C++ workload, PowerShell 7, LLVM (`llvm-ar` and `lld-link`), and GitHub CLI (`gh`).

Run `build_release_app.bat`. It builds the patched Avalonia Windows backend, then publishes `UnVaultLauncher.exe` to the repository root. LLVM is expected in `%ProgramFiles%\LLVM\bin` or on PATH. For direct `dotnet publish Source/App -p:PublishProfile=win-x64`, build the patched backend first and put LLVM on PATH.

The EXE runs without adjacent application DLLs or a .NET installation. Embedded sign-in still requires the installed WebView2 Runtime. Static graphics binaries come from the pinned `StaticLink.Avalonia` package; the patched Avalonia backend and deferred WARP-to-hardware rendering remain in use.

Native checks: [graphics](Tests/Graphics/README.md), [WebView2](Tests/Browser/README.md).
