using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenScct.Project;

namespace OpenScct.Launcher.App;

/// <summary>Project links and report details, independent of the launcher's active operation.</summary>
internal sealed class HelpForm : Form
{
    private readonly ReportContext _context;
    private readonly ThemePalette _palette;
    private readonly Func<string, bool> _open;
    private readonly Action<string> _copy;
    private bool IsEditor => _palette == ThemePalette.Editor;
    private readonly ProjectLinkActions _actions;
    private readonly TextBox _details = Theme.TextBox();
    private readonly TextBox _address = Theme.TextBox();
    private readonly Label _message = Theme.Label(string.Empty, Theme.Small, Theme.Muted);
    private readonly Button _reportBug;
    private readonly Panel _scroll;

    internal HelpForm(ReportContext context, Func<string, bool>? open = null, Action<string>? copy = null,
        ThemePalette? palette = null, string? editorVersion = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _palette = palette ?? ThemePalette.Game;
        _open = open ?? OpenBrowser;
        _copy = copy ?? Clipboard.SetText;
        _actions = new ProjectLinkActions(_open, _copy);

        Text = "Help & feedback";
        Name = "ProjectHelp";
        Icon = IsEditor ? Theme.LoadEditorIcon(32) : Theme.LoadIcon(32);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(660, 600);
        MinimumSize = new Size(480, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        var close = Theme.Button("&Close");
        close.Name = "CloseHelp";
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;
        FlowLayoutPanel footer = Theme.Flow(false, close);
        footer.Dock = DockStyle.Bottom;
        footer.FlowDirection = FlowDirection.RightToLeft;
        footer.Padding = new Padding(16, 8, 16, 12);

        _scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(16, 16, 16, 0),
            BackColor = Theme.Background,
        };
        TableLayoutPanel root = Theme.Grid(Theme.Fill);
        root.Dock = DockStyle.Top;

        void Row(Control control, int gap = 8)
        {
            control.Margin = new Padding(0, gap, 0, 0);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Theme.AddRow(root, control);
        }

        Row(Theme.Label(IsEditor ? "Editor help && feedback" : "Help && feedback", Theme.Heading, Theme.Accent), 0);
        Row(Theme.Wrapping(Theme.Label(IsEditor
            ? "Editor documentation and reports open on GitHub. For installation or launcher problems, use Launcher help."
            : "Links open in your browser. Bug reports use the website with Discord sign-in; feature suggestions and pull requests require GitHub sign-in.", Theme.Body, Theme.Muted)));

        _reportBug = LinkButton("Report a &bug", ProjectDestination.BugReport);
        Row(Links(_reportBug,
            LinkButton("Suggest a &feature", ProjectDestination.FeatureRequest),
            LinkButton("Browse existing &issues", ProjectDestination.Issues)));
        if (IsEditor)
        {
            Button launcherHelp = Theme.Button("Launcher help");
            launcherHelp.Name = "LauncherHelp";
            launcherHelp.Click += (_, _) =>
            {
                bool opened = _actions.Open(ProjectDestination.Help, _context);
                _address.Text = _actions.TargetUrl;
                _address.Visible = !opened;
                ShowResult(opened);
            };
            Row(launcherHelp);
        }

        var copyDetails = Theme.Button("Copy d&etails");
        copyDetails.Name = "CopyDetails";
        copyDetails.Click += (_, _) => CopyDetails();
        TableLayoutPanel detailsHeader = Theme.Grid(Theme.Fill, Theme.Auto);
        Theme.AddRow(detailsHeader, Theme.Wrapping(Theme.Label("Version details for your report", Theme.Heading)), copyDetails);
        Row(detailsHeader, 16);
        Row(Theme.Wrapping(Theme.Label("The launcher includes these version details only. It does not attach logs, account details or local paths.", Theme.Body, Theme.Muted)));
        _details.Name = "ReportDetails";
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.Height = 116;
        _details.Text = ProjectLinks.EnvironmentDetails(context).Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        if (IsEditor) _details.Text = "Editor version: " + (editorVersion ?? "Not installed") + Environment.NewLine + _details.Text;
        _details.AccessibleName = "Version details for your report";
        Row(_details);

        Row(Theme.Label("Contribute", Theme.Heading, Theme.Accent), 16);
        Row(Theme.Wrapping(Theme.Label("Use your fork or a branch you can push to, then open a pull request on GitHub.", Theme.Body, Theme.Muted)));
        Row(Links(LinkButton("&Documentation", ProjectDestination.Documentation),
            LinkButton("Contributing &guide", ProjectDestination.Contributing),
            LinkButton("Developer set&up", ProjectDestination.Development),
            LinkButton("&Pull requests", ProjectDestination.PullRequests),
            LinkButton("&New pull request", ProjectDestination.NewPullRequest)));

        _address.Name = "SelectedLink";
        _address.ReadOnly = true;
        _address.AccessibleName = "Browser address";
        _address.Visible = false;
        Row(_address, 4);

        _message.Name = "HelpStatus";
        _message.AccessibleRole = AccessibleRole.Alert;
        Row(Theme.Wrapping(_message));
        _scroll.Controls.Add(root);
        Controls.Add(_scroll);
        Controls.Add(footer);
        Theme.Apply(this, _palette);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _reportBug.Focus();
    }

    private Button LinkButton(string label, ProjectDestination destination)
    {
        Button button = Theme.Button(label);
        button.Name = destination.ToString();
        button.Click += (_, _) =>
        {
            if (IsEditor)
            {
                string url = EditorUrl(destination);
                bool editorOpened = false;
                try { editorOpened = _open(url); }
                catch (Exception ex) when (ex is ExternalException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { }
                _address.Text = url;
                _address.Visible = !editorOpened;
                ShowMessage(editorOpened, editorOpened ? string.Empty : "Could not open your browser. Copy the link and open it in your browser.");
                return;
            }
            bool opened = _actions.Open(destination, _context);
            _address.Text = _actions.TargetUrl;
            _address.Visible = !opened;
            ShowResult(opened);
        };
        return button;
    }

    private static FlowLayoutPanel Links(params Control[] buttons)
    {
        FlowLayoutPanel flow = Theme.Flow(false, buttons);
        flow.WrapContents = true;
        flow.Dock = DockStyle.Fill;
        return flow;
    }

    private void ShowResult(bool success)
    {
        ShowMessage(success, _actions.Message);
    }

    private void ShowMessage(bool success, string message)
    {
        _message.Text = message;
        _message.ForeColor = success ? _palette.Accent : Theme.Warning;
        if (_message.Text.Length > 0)
        {
            _scroll.PerformLayout();
            _scroll.ScrollControlIntoView(_message);
        }
    }

    private void CopyDetails()
    {
        if (!IsEditor) { ShowResult(_actions.CopyDetails(_context)); return; }
        try { _copy(_details.Text); ShowMessage(true, "Details copied."); }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            ShowMessage(false, "Could not copy. Select the text and copy it manually.");
        }
    }

    private string EditorUrl(ProjectDestination destination)
    {
        const string repo = "https://github.com/Lumbridge/OpenSCCT-Editor";
        return destination switch
        {
            ProjectDestination.BugReport => repo + "/issues/new?body=" + Uri.EscapeDataString(_details.Text),
            ProjectDestination.FeatureRequest => repo + "/issues/new",
            ProjectDestination.Issues => repo + "/issues",
            ProjectDestination.Documentation => repo + "/blob/main/README.md",
            ProjectDestination.Contributing => repo + "/blob/main/CONTRIBUTING.md",
            ProjectDestination.Development => repo + "/blob/main/README.md#build-from-source",
            ProjectDestination.PullRequests => repo + "/pulls",
            ProjectDestination.NewPullRequest => repo + "/compare?expand=1",
            _ => repo,
        };
    }

    private static bool OpenBrowser(string url)
    {
        try
        {
            // Shell execution can reuse an existing browser and return no process.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
