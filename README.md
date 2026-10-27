# UnVault Launcher - Time-to-First-Frame optimizations

- NativeAOT compatible (single EXE with statically linked Skia, HarfBuzz, ANGLE and WebView2 Loader; no SFX on startup). The initial NativeAOT conversion saved about 200 ms versus the SFX version.
- A trick I stole from @thomasklemenc 's TaskSlinger (https://taskslinger.net/) : create a WARP device first and show the first frame, while creating the hardware device in the background. When ready, switch to the hardware one. This requires a patched Avalonia build (the build scripts are modified to do this automatically). ~300ms -> 40-50ms

- Other minor tweaks.

- The window is revealed after its first successful native composition commit, rather than waiting for another Avalonia render pass. WARP-to-hardware resource replacement is held until the window has been revealed.

- WARP shader-program binaries are cached locally in `%LOCALAPPDATA%\UnVaultLauncher\graphics\warp-programs-v1.bin`. ANGLE checks environment compatibility; misses or rejected binaries are recompiled and replace the cached result. Rendering callbacks only access memory, and background persistence starts after the first frame is revealed. The cache is bounded to 128 entries / 4 MiB of payload, with oldest compiled entries evicted. Hardware rendering does not use this cache. Set `UNVAULT_WARP_CACHE=0` to disable it for startup comparisons. No prebuilt cache is bundled.

## Build

Requires .NET 10, Visual Studio 2022 or later with the Desktop development with C++ workload, PowerShell 7, LLVM (`llvm-ar` and `lld-link`), and GitHub CLI (`gh`).

Run `build_release_app.bat`. It builds the patched Avalonia Windows backend, then publishes `UnVaultLauncher.exe` to the repository root. LLVM is expected in `%ProgramFiles%\LLVM\bin` or on PATH. For direct `dotnet publish Source/App -p:PublishProfile=win-x64`, build the patched backend first and put LLVM on PATH.

The backend helper uses a separate source checkout under `Bin/dependencies/Avalonia-<patched-package-version>` for each patch revision, leaving older checkouts untouched. The patch and package references must be updated together.

The EXE runs without adjacent application DLLs or a .NET installation. Embedded sign-in still requires the installed WebView2 Runtime. Static graphics binaries come from the pinned `StaticLink.Avalonia` package; the patched Avalonia backend and deferred WARP-to-hardware rendering remain in use.

Native checks: [graphics](Tests/Graphics/README.md), [WebView2](Tests/Browser/README.md).
