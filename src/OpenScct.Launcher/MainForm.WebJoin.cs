using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.App;

internal sealed partial class MainForm
{
    private string? _pendingWebJoin;
    private readonly string? _protocolProblem;
    private bool _webJoinReady;
    private bool _handlingWebJoin;

    public void QueueWebJoin(string serverId)
    {
        if (!WebJoinRequest.IsValidServerId(serverId) || IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => QueueWebJoin(serverId)); }
            catch (InvalidOperationException) { /* The window closed during the handoff. */ }
            return;
        }
        _pendingWebJoin = serverId;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        RefreshLocal();
        if (_work is not null) SetStatus("Your Join request is waiting. Finish the current operation, then continue in the game.", Theme.Muted);
        else if (_webJoinReady) _ = TryWebJoinAsync();
    }

    private async Task TryWebJoinAsync()
    {
        if (_handlingWebJoin || !_webJoinReady || _work is not null || _pendingWebJoin is not { } serverId || IsDisposed) return;
        _handlingWebJoin = true;
        try
        {
            if (await WebJoinPipe.SendAsync(WebJoinPipe.GameName, serverId))
            {
                if (_pendingWebJoin == serverId) _pendingWebJoin = null;
                if (IsDisposed) return;
                SetStatus("Join request sent to OpenSCCT. Continue in the game; an active match will not be interrupted.", Theme.Accent);
                WindowState = FormWindowState.Minimized;
                return;
            }
            if (IsDisposed || _work is not null) return;
            if (_games.Running().Count > 0)
            {
                SetStatus("The running game could not receive your Join request. Close it and update OpenSCCT, then choose JOIN SESSION here.", Theme.Warning);
                return;
            }
            SemVersion? current = _store.Current();
            if (current is null)
            {
                SetStatus("Your Join request is saved for this launcher session. Install OpenSCCT and get the game files using the steps above.", Theme.Warning);
                return;
            }
            if (current < WebJoinRequest.MinimumGameVersion)
            {
                SetStatus($"Website Join needs OpenSCCT {WebJoinRequest.MinimumGameVersion} or later. Check for updates and update the game; your Join request will wait here.", Theme.Warning);
                return;
            }
            if (!FilesReady(current) && !DownloadedFilesPlayable(current) && !(GameFound && BuiltFilesPlayable(current)))
            {
                SetStatus("Get or prepare the game files in step 3. Your Join request will continue when they are ready.", Theme.Warning);
                return;
            }
            if (_pendingWebJoin != serverId) return;
            if (Play(serverId)) _pendingWebJoin = null;
        }
        finally
        {
            _handlingWebJoin = false;
            if (!IsDisposed)
            {
                RefreshLocal();
                if (_pendingWebJoin is not null && _pendingWebJoin != serverId) _ = TryWebJoinAsync();
            }
        }
    }
}
