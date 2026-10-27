# Static graphics smoke check

Requires Windows, the installed WebView2 Runtime, .NET 10, the Visual Studio C++ tools, PowerShell 7, and LLVM (`llvm-ar` and `lld-link` on PATH). Build the patched Avalonia backend first with `dotnet Build/Avalonia/BuildPatchedAvalonia.cs`.

Publish before testing:

```
dotnet publish Tests/Graphics -c Release -r win-x64 -warnaserror
```

Run the resulting EXE **sequentially**, in an otherwise empty directory, in each mode:

```
UnVault.Graphics.Smoke.exe deferred
UnVault.Graphics.Smoke.exe warp
UnVault.Graphics.Smoke.exe hardware
```

The check round-trips a JPEG and exercises truncated-image decoder recovery, displays a small animated window with shaped text, and resizes it. Only ANGLE rendering is allowed; software-renderer fallback is an error. Deferred mode additionally requires the patched backend's successful WARP-to-hardware switch and checks that it follows the window reveal. Finally it opens a local page in an embedded WebView2 and executes JavaScript, checking that both static native stacks work in the same process. No account or screenshots are used. Hardware and deferred modes require a working hardware adapter. Run on a desktop session, not a headless CI host.

## First-frame fence checks

Build `Build/Avalonia/FirstFrameSmoke.cs` with `dotnet build Build/Avalonia/FirstFrameSmoke.cs -c Release`, then run these modes serially:

```
dotnet run --file Build/Avalonia/FirstFrameSmoke.cs -c Release --no-build -- reveal
dotnet run --file Build/Avalonia/FirstFrameSmoke.cs -c Release --no-build -- busy-ui
dotnet run --file Build/Avalonia/FirstFrameSmoke.cs -c Release --no-build -- hardware
dotnet run --file Build/Avalonia/FirstFrameSmoke.cs -c Release --no-build -- direct-composition
dotnet run --file Build/Avalonia/FirstFrameSmoke.cs -c Release --no-build -- multiple-windows
dotnet run --file Build/Avalonia/FirstFrameSmoke.cs -c Release --no-build -- close-before-show
dotnet run --file Build/Avalonia/FirstFrameSmoke.cs -c Release --no-build -- close-early
```

These deliberately delay the first render-thread draw, verify that neither native commit readiness nor the reveal is reported during that draw, check hide/show and shutdown, and exercise per-window fences and hardware deferral across multiple windows. `busy-ui` additionally blocks the UI dispatcher before the first native commit and requires the window to become uncloaked while that dispatcher is still blocked. The WinUIComposition backend exposes a native commit fence; other presentation backends retain the conservative two-render-turn fallback. The fence confirms native submission, not physical display scan-out.
