using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace OpenScctGame.Web;

[SupportedOSPlatform("browser")]
internal static partial class BrowserExports
{
    [JSImport("globalThis.openscctRuntimeReady")]
    internal static partial void Ready();

    [JSImport("globalThis.openscctGameFailed")]
    internal static partial void BootFailed(string message);

    [JSImport("globalThis.openscctReportState")]
    internal static partial void ReportState(string state);

    [JSImport("globalThis.openscctOpenAccount")]
    internal static partial void OpenAccount();

    [JSImport("globalThis.openscctSignedOut")]
    internal static partial void ReloadAfterSignOut();

    [JSImport("globalThis.openscctOpenMultiplayer")]
    internal static partial void OpenMultiplayer();

    [JSImport("globalThis.openscctOnlineStatus")]
    internal static partial void OnlineStatus(string message);

    [JSExport]
    public static Task JoinServer(string serverId, string password, string inviteKey) => BrowserAccount.Join(serverId, password, inviteKey);

    [JSExport]
    public static void LessonAction(string action)
    {
        if (action != "reset" && BrowserPlatform.CurrentPlay?.Controls is { } controls)
        {
            controls.RequireCapture = false;
            BrowserPlatform.InputActivated = true;
        }
        BrowserPlatform.LessonAction?.Invoke(action);
    }

    [JSExport]
    public static void StartRound(string kind) => BrowserPlatform.StartRound?.Invoke(kind);

    [JSExport]
    public static void OpenMenu()
    {
        DeactivateControls();
        if (BrowserPlatform.CurrentPlay is { } play && Godot.GodotObject.IsInstanceValid(play)) play.Menu?.Open();
    }

    [JSExport]
    public static void FlushSettings() => BrowserPlatform.FlushSettings?.Invoke();

    [JSExport]
    public static void UseAccountSettings() => BrowserAccount.UseAccountSettings();

    [JSExport]
    public static void ActivateControls()
    {
        if (BrowserPlatform.CurrentPlay?.Controls is { } controls)
        {
            // Keyboard play remains available in embedded browsers that cannot grant pointer lock.
            controls.RequireCapture = false;
            controls.CapturePointer = true;
            BrowserPlatform.InputActivated = true;
            OpenScctGame.Controls.Pointer.Capture();
        }
    }

    [JSExport]
    public static void DeactivateControls()
    {
        if (BrowserPlatform.CurrentPlay?.Controls is { } controls) controls.RequireCapture = true;
        BrowserPlatform.InputActivated = false;
        OpenScctGame.Controls.BindingInput.Current?.ResetAll();
        OpenScctGame.Controls.Pointer.Release();
    }
}
