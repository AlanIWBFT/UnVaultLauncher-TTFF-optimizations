# Static Skia software smoke checks

Requires Windows, the installed WebView2 Runtime, .NET 10, the Visual Studio C++ tools, PowerShell 7, and LLVM (`llvm-ar` and `lld-link` on PATH). Uses official Avalonia 12.1.3; no patched backend build is required.

Publish before running the check:

```
dotnet publish Tests/Graphics -c Release -r win-x64 -warnaserror
```

Run `UnVault.Graphics.Smoke.exe` from the native publish folder on a desktop session. It round-trips a JPEG and exercises truncated-image decoder recovery, checks that the native background brush matches `#141414` before the first frame, displays shaped text, animates and resizes the software-rendered window, and verifies that rendering does not load D3D11, WARP or dynamic ANGLE. It then opens a local page in WebView2 and executes JavaScript in the same process. WebView2 may independently load GPU libraries, so the module check precedes browser creation. No account or screenshots are used.
