# WebView2 NativeAOT smoke check

Requires Windows, the installed WebView2 Runtime, .NET 10 SDK, and the Visual Studio C++ tools used to publish the app.

Publish first, then run the native executable (from the repository root):

```
dotnet publish Tests/Browser -c Release -r win-x64 -warnaserror
Bin\publish\UnVault.Browser.Smoke\release_win-x64\UnVault.Browser.Smoke.exe
```

This exercises the real source-generated COM boundary and statically linked loader. It uses a hidden native window, a temporary browser profile, and a local HTML page; no Epic account is needed. Checks cover navigation, UTF-16 script results, callbacks after GC, resizing, cancelled navigation, handled popups, pending operations at close, reopening, and missing-runtime errors. Profiles remain under `%TEMP%/opencode/unvault-com-smoke` for inspection.

The interfaces in `Source/App/Browser/WebViewInterfaces.cs` follow the pinned WebView2 SDK's `WebView2.idl`. Preserve every inherited vtable slot when editing them. Rerun this check after SDK or interop changes. Managed builds use the dynamic loader copied by `WebView2.targets`; published NativeAOT executables must work without either `WebView2Loader.dll` or `Microsoft.Web.WebView2.Core.dll`.

Afterward, test actual sign-in in the published app, including social-login popups, authorization-code completion, resizing, closing while loading, and signing out then back in.
