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

The check round-trips a JPEG and exercises truncated-image decoder recovery, displays a small animated window with shaped text, and resizes it. Only ANGLE rendering is allowed; software-renderer fallback is an error. Deferred mode additionally requires the patched backend's successful WARP-to-hardware switch. Finally it opens a local page in an embedded WebView2 and executes JavaScript, checking that both static native stacks work in the same process. No account or screenshots are used. Hardware and deferred modes require a working hardware adapter. Run on a desktop session, not a headless CI host.
