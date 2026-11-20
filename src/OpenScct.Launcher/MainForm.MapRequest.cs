using System;
using System.Linq;
using System.Windows.Forms;
using OpenScct.Launcher.Launch;

namespace OpenScct.Launcher.App;

internal sealed partial class MainForm
{
    private string? _deferredMap;
    private Form? _mapWaitingFor;

    internal void OpenMapRequest(string code)
    {
        if (!WebMapRequest.IsValidCode(code) || IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => OpenMapRequest(code)); } catch (InvalidOperationException) { }
            return;
        }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        if (OwnedForms.FirstOrDefault(f => f.Modal && f.Visible) is { } modal)
        {
            if (modal is MapsForm maps)
            {
                maps.ReviewMap(code);
                maps.Activate();
            }
            else
            {
                _deferredMap = code;
                if (_mapWaitingFor != modal)
                {
                    _mapWaitingFor = modal;
                    modal.FormClosed += (_, _) =>
                    {
                        _mapWaitingFor = null;
                        if (_deferredMap is not { } pending || IsDisposed || Disposing) return;
                        _deferredMap = null;
                        BeginInvoke(() => OpenMapRequest(pending));
                    };
                }
            }
            return;
        }
        _page = LibraryPage.Mods;
        SelectProduct(_selected is Product.Unreal or Product.Editor ? _selected : Product.Game);
        Activate();
        ShowMapBrowser(code);
    }

    private void ShowMapBrowser(string? requestedCode = null)
    {
        if (_work is not null && requestedCode is null) return;
        using var dialog = new MapsForm(Http, _layout.Packs, Palette,
            () => _work is not null || EditorUsesSharedContent() || _unreal.IsRunning || _games.Running().Count > 0);
        if (requestedCode is not null) dialog.ReviewMap(requestedCode);
        dialog.ShowDialog(this);
        RefreshLocal();
    }
}
