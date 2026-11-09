# WebView2 NativeAOT smoke check

Requires Windows, the installed WebView2 Runtime, .NET 10 SDK and the Visual Studio C++ tools.

Publish `Tests/Browser` with `dotnet publish Tests/Browser -c Release -r win-x64 -warnaserror`, then run the native executable from its publish folder with `WebView2Loader.dll` beside it. This checks the source-generated COM boundary, navigation, UTF-16 scripts, callbacks after GC, resize, cancellation, popups, close/reopen and missing-runtime handling using local pages and a temporary browser profile. No Epic account is needed.

The loader is dynamic at this stage; no managed WebView2 SDK wrapper is used. Keep the inherited vtable slots in `Source/App/Browser/WebViewInterfaces.cs` aligned with the pinned SDK's IDL.
