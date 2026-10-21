using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace UnVault.App.Browser;

// ABI declarations from Microsoft.Web.WebView2 1.0.4258.31 / WebView2.idl.
// Keep every method in SDK order, including unused methods, to preserve inherited vtable slots.
// Unused interface parameters are opaque pointers; BOOL is a 32-bit int, tokens are 64-bit values.
// These interfaces only wrap native objects. Callback interfaces below also support managed implementations.
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("76eceacb-0462-4d94-ac83-423a6793775e")]
internal partial interface ICoreWebView2
{
    ICoreWebView2Settings GetSettings();
    string GetSource();
    void Navigate(string uri);
    void NavigateToString(string html);
    long AddNavigationStarting(INavigationStartingHandler handler);
    void RemoveNavigationStarting(long token);
    long AddContentLoading(nint handler);
    void RemoveContentLoading(long token);
    long AddSourceChanged(ISourceChangedHandler handler);
    void RemoveSourceChanged(long token);
    long AddHistoryChanged(nint handler);
    void RemoveHistoryChanged(long token);
    long AddNavigationCompleted(INavigationCompletedHandler handler);
    void RemoveNavigationCompleted(long token);
    long AddFrameNavigationStarting(nint handler);
    void RemoveFrameNavigationStarting(long token);
    long AddFrameNavigationCompleted(nint handler);
    void RemoveFrameNavigationCompleted(long token);
    long AddScriptDialogOpening(nint handler);
    void RemoveScriptDialogOpening(long token);
    long AddPermissionRequested(nint handler);
    void RemovePermissionRequested(long token);
    long AddProcessFailed(nint handler);
    void RemoveProcessFailed(long token);
    void AddScriptToExecuteOnDocumentCreated(string script, nint handler);
    void RemoveScriptToExecuteOnDocumentCreated(string id);
    void ExecuteScript(string script, IExecuteScriptHandler handler);
    void CapturePreview(int format, nint stream, nint handler);
    void Reload();
    void PostWebMessageAsJson(string json);
    void PostWebMessageAsString(string text);
    long AddWebMessageReceived(nint handler);
    void RemoveWebMessageReceived(long token);
    void CallDevToolsProtocolMethod(string method, string parameters, nint handler);
    uint GetBrowserProcessId();
    int GetCanGoBack();
    int GetCanGoForward();
    void GoBack();
    void GoForward();
    nint GetDevToolsProtocolEventReceiver(string name);
    void Stop();
    long AddNewWindowRequested(INewWindowRequestedHandler handler);
    void RemoveNewWindowRequested(long token);
    long AddDocumentTitleChanged(nint handler);
    void RemoveDocumentTitleChanged(long token);
    string GetDocumentTitle();
    void AddHostObjectToScript(string name, nint variant);
    void RemoveHostObjectFromScript(string name);
    void OpenDevToolsWindow();
    long AddContainsFullScreenElementChanged(nint handler);
    void RemoveContainsFullScreenElementChanged(long token);
    int GetContainsFullScreenElement();
    long AddWebResourceRequested(nint handler);
    void RemoveWebResourceRequested(long token);
    void AddWebResourceRequestedFilter(string uri, int context);
    void RemoveWebResourceRequestedFilter(string uri, int context);
    long AddWindowCloseRequested(nint handler);
    void RemoveWindowCloseRequested(long token);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("9e8f0cf8-e670-4b5e-b2bc-73e061e3184c")]
internal partial interface ICoreWebView2_2 : ICoreWebView2
{
    long AddWebResourceResponseReceived(nint handler);
    void RemoveWebResourceResponseReceived(long token);
    void NavigateWithWebResourceRequest(nint request);
    long AddDOMContentLoaded(nint handler);
    void RemoveDOMContentLoaded(long token);
    nint GetCookieManager();
    nint GetEnvironment();
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("a0d6df20-3b92-416d-aa0c-437a9c727857")]
internal partial interface ICoreWebView2_3 : ICoreWebView2_2
{
    void TrySuspend(nint handler);
    void Resume();
    int GetIsSuspended();
    void SetVirtualHostNameToFolderMapping(string host, string folder, int access);
    void ClearVirtualHostNameToFolderMapping(string host);
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("20d02d59-6df2-42dc-bd06-f98a694b1302")]
internal partial interface ICoreWebView2_4 : ICoreWebView2_3
{
    long AddFrameCreated(nint handler);
    void RemoveFrameCreated(long token);
    long AddDownloadStarting(nint handler);
    void RemoveDownloadStarting(long token);
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("bedb11b8-d63c-11eb-b8bc-0242ac130003")]
internal partial interface ICoreWebView2_5 : ICoreWebView2_4
{
    long AddClientCertificateRequested(nint handler);
    void RemoveClientCertificateRequested(long token);
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("499aadac-d92c-4589-8a75-111bfc167795")]
internal partial interface ICoreWebView2_6 : ICoreWebView2_5 { void OpenTaskManagerWindow(); }
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("79c24d83-09a3-45ae-9418-487f32a58740")]
internal partial interface ICoreWebView2_7 : ICoreWebView2_6 { void PrintToPdf(string path, nint settings, nint handler); }
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("e9632730-6e1e-43ab-b7b8-7b2c9e62e094")]
internal partial interface ICoreWebView2_8 : ICoreWebView2_7
{
    long AddIsMutedChanged(nint handler);
    void RemoveIsMutedChanged(long token);
    int GetIsMuted();
    void SetIsMuted(int value);
    long AddIsDocumentPlayingAudioChanged(nint handler);
    void RemoveIsDocumentPlayingAudioChanged(long token);
    int GetIsDocumentPlayingAudio();
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("4d7b2eab-9fdc-468d-b998-a9260b5ed651")]
internal partial interface ICoreWebView2_9 : ICoreWebView2_8
{
    long AddIsDefaultDownloadDialogOpenChanged(nint handler);
    void RemoveIsDefaultDownloadDialogOpenChanged(long token);
    int GetIsDefaultDownloadDialogOpen();
    void OpenDefaultDownloadDialog();
    void CloseDefaultDownloadDialog();
    int GetDefaultDownloadDialogCornerAlignment();
    void SetDefaultDownloadDialogCornerAlignment(int value);
    NativePoint GetDefaultDownloadDialogMargin();
    void SetDefaultDownloadDialogMargin(NativePoint value);
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("b1690564-6f5a-4983-8e48-31d1143fecdb")]
internal partial interface ICoreWebView2_10 : ICoreWebView2_9
{
    long AddBasicAuthenticationRequested(nint handler);
    void RemoveBasicAuthenticationRequested(long token);
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("0be78e56-c193-4051-b943-23b460c08bdb")]
internal partial interface ICoreWebView2_11 : ICoreWebView2_10
{
    void CallDevToolsProtocolMethodForSession(string session, string method, string parameters, nint handler);
    long AddContextMenuRequested(nint handler);
    void RemoveContextMenuRequested(long token);
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("35d69927-bcfa-4566-9349-6b3e0d154cac")]
internal partial interface ICoreWebView2_12 : ICoreWebView2_11
{
    long AddStatusBarTextChanged(nint handler);
    void RemoveStatusBarTextChanged(long token);
    string GetStatusBarText();
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("f75f09a8-667e-4983-88d6-c8773f315e84")]
internal partial interface ICoreWebView2_13 : ICoreWebView2_12 { nint GetProfile(); }
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("6daa4f10-4a90-4753-8898-77c5df534165")]
internal partial interface ICoreWebView2_14 : ICoreWebView2_13
{
    long AddServerCertificateErrorDetected(nint handler);
    void RemoveServerCertificateErrorDetected(long token);
    void ClearServerCertificateErrorActions(nint handler);
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("517b2d1d-7dae-4a66-a4f4-10352ffb9518")]
internal partial interface ICoreWebView2_15 : ICoreWebView2_14
{
    long AddFaviconChanged(nint handler);
    void RemoveFaviconChanged(long token);
    string GetFaviconUri();
    void GetFavicon(int format, nint handler);
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("0eb34dc9-9f91-41e1-8639-95cd5943906b")]
internal partial interface ICoreWebView2_16 : ICoreWebView2_15
{
    void Print(nint settings, nint handler);
    void ShowPrintUI(int kind);
    void PrintToPdfStream(nint settings, nint handler);
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("702e75d4-fd44-434d-9d70-1a68a6b1192a")]
internal partial interface ICoreWebView2_17 : ICoreWebView2_16 { void PostSharedBufferToScript(nint buffer, int access, string data); }
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("7a626017-28be-49b2-b865-3ba2b3522d90")]
internal partial interface ICoreWebView2_18 : ICoreWebView2_17
{
    long AddLaunchingExternalUriScheme(IExternalUriHandler handler);
    void RemoveLaunchingExternalUriScheme(long token);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("b96d755e-0319-4e92-a296-23436f46a1fc")]
internal partial interface ICoreWebView2Environment
{
    void CreateCoreWebView2Controller(nint parent, IControllerCompletedHandler handler);
    nint CreateWebResourceResponse(nint stream, int status, string reason, string headers);
    string GetBrowserVersionString();
    long AddNewBrowserVersionAvailable(nint handler);
    void RemoveNewBrowserVersionAvailable(long token);
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("4d00c0d1-9434-4eb6-8078-8697a560334f")]
internal partial interface ICoreWebView2Controller
{
    int GetIsVisible();
    void SetIsVisible(int value);
    NativeRect GetBounds();
    void SetBounds(NativeRect value);
    double GetZoomFactor();
    void SetZoomFactor(double value);
    long AddZoomFactorChanged(nint handler);
    void RemoveZoomFactorChanged(long token);
    void SetBoundsAndZoomFactor(NativeRect bounds, double zoom);
    void MoveFocus(int reason);
    long AddMoveFocusRequested(nint handler);
    void RemoveMoveFocusRequested(long token);
    long AddGotFocus(nint handler);
    void RemoveGotFocus(long token);
    long AddLostFocus(nint handler);
    void RemoveLostFocus(long token);
    long AddAcceleratorKeyPressed(nint handler);
    void RemoveAcceleratorKeyPressed(long token);
    nint GetParentWindow();
    void SetParentWindow(nint value);
    void NotifyParentWindowPositionChanged();
    void Close();
    ICoreWebView2 GetCoreWebView2();
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("c979903e-d4ca-4228-92eb-47ee3fa96eab")]
internal partial interface ICoreWebView2Controller2 : ICoreWebView2Controller
{
    NativeColor GetDefaultBackgroundColor();
    void SetDefaultBackgroundColor(NativeColor value);
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect { public int Left, Top, Right, Bottom; }
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint { public int X, Y; }
[StructLayout(LayoutKind.Sequential)]
internal struct NativeColor { public byte A, R, G, B; }

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("e562e4f0-d7fa-43ac-8d71-c05150499f00")]
internal partial interface ICoreWebView2Settings
{
    int GetIsScriptEnabled();
    void SetIsScriptEnabled(int value);
    int GetIsWebMessageEnabled();
    void SetIsWebMessageEnabled(int value);
    int GetAreDefaultScriptDialogsEnabled();
    void SetAreDefaultScriptDialogsEnabled(int value);
    int GetIsStatusBarEnabled();
    void SetIsStatusBarEnabled(int value);
    int GetAreDevToolsEnabled();
    void SetAreDevToolsEnabled(int value);
    int GetAreDefaultContextMenusEnabled();
    void SetAreDefaultContextMenusEnabled(int value);
    int GetAreHostObjectsAllowed();
    void SetAreHostObjectsAllowed(int value);
    int GetIsZoomControlEnabled();
    void SetIsZoomControlEnabled(int value);
    int GetIsBuiltInErrorPageEnabled();
    void SetIsBuiltInErrorPageEnabled(int value);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("5b495469-e119-438a-9b18-7604f25f2e49")]
internal partial interface INavigationStartingArgs
{
    string GetUri();
    int GetIsUserInitiated();
    int GetIsRedirected();
    nint GetRequestHeaders();
    int GetCancel();
    void SetCancel(int value);
    ulong GetNavigationId();
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("34acb11c-fc37-4418-9132-f9c21d1eafb9")]
internal partial interface INewWindowRequestedArgs
{
    string GetUri();
    void SetNewWindow(nint value);
    nint GetNewWindow();
    void SetHandled(int value);
    int GetHandled();
    int GetIsUserInitiated();
    nint GetDeferral();
    nint GetWindowFeatures();
}
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper, StringMarshalling = StringMarshalling.Utf16)]
[Guid("07d1a6c3-7175-4ba1-9306-e593ca07e46c")]
internal partial interface IExternalUriArgs
{
    string GetUri();
    string GetInitiatingOrigin();
    int GetIsUserInitiated();
    int GetCancel();
    void SetCancel(int value);
    nint GetDeferral();
}

[GeneratedComInterface, Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d")]
internal partial interface IEnvironmentCompletedHandler { void Invoke(int error, ICoreWebView2Environment? environment); }
[GeneratedComInterface, Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c")]
internal partial interface IControllerCompletedHandler { void Invoke(int error, ICoreWebView2Controller? controller); }
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("49511172-cc67-4bca-9923-137112f4c4cc")]
internal partial interface IExecuteScriptHandler { void Invoke(int error, string? result); }
[GeneratedComInterface, Guid("9adbe429-f36d-432b-9ddc-f8881fbd76e3")]
internal partial interface INavigationStartingHandler { void Invoke(nint sender, INavigationStartingArgs args); }
[GeneratedComInterface, Guid("d33a35bf-1c49-4f98-93ab-006e0533fe1c")]
internal partial interface INavigationCompletedHandler { void Invoke(nint sender, nint args); }
[GeneratedComInterface, Guid("3c067f9f-5388-4772-8b48-79f7ef1ab37c")]
internal partial interface ISourceChangedHandler { void Invoke(nint sender, nint args); }
[GeneratedComInterface, Guid("d4c185fe-c81c-4989-97af-2d3fa7ab5651")]
internal partial interface INewWindowRequestedHandler { void Invoke(nint sender, INewWindowRequestedArgs args); }
[GeneratedComInterface, Guid("74f712e0-8165-43a9-a13f-0cce597e75df")]
internal partial interface IExternalUriHandler { void Invoke(nint sender, IExternalUriArgs args); }
