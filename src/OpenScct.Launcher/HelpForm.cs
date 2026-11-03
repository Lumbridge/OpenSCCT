using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OpenScct.Project;

namespace OpenScct.Launcher.App;

/// <summary>Project links and report details, independent of the launcher's active operation.</summary>
internal sealed class HelpForm : Form
{
    private readonly ReportContext _context;
    private readonly ProjectLinkActions _actions;
    private readonly TextBox _details = Theme.TextBox();
    private readonly TextBox _address = Theme.TextBox();
    private readonly Label _message = Theme.Label(string.Empty, Theme.Small, Theme.Muted);
    private readonly Button _copyLink = Theme.Button("Copy &link");
    private readonly Button _reportBug;
    private readonly Panel _scroll;

    internal HelpForm(ReportContext context, Func<string, bool>? open = null, Action<string>? copy = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _actions = new ProjectLinkActions(open ?? OpenBrowser, copy ?? Clipboard.SetText);

        Text = "Help & contribute";
        Name = "ProjectHelp";
        Icon = Theme.LoadIcon(32);
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

        Row(Theme.Label("Help && contribute", Theme.Heading, Theme.Accent), 0);
        Row(Theme.Wrapping(Theme.Label("Links open in your browser. Bug reports use the website with Discord sign-in; feature suggestions and pull requests require GitHub sign-in.", Theme.Body, Theme.Muted)));

        _reportBug = LinkButton("Report a &bug", ProjectDestination.BugReport);
        Row(Links(_reportBug,
            LinkButton("Suggest a &feature", ProjectDestination.FeatureRequest),
            LinkButton("Browse existing &issues", ProjectDestination.Issues)));

        var copyDetails = Theme.Button("Copy d&etails");
        copyDetails.Name = "CopyDetails";
        copyDetails.Click += (_, _) => ShowResult(_actions.CopyDetails(_context));
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
        _details.AccessibleName = "Version details for your report";
        Row(_details);

        Row(Theme.Label("Contribute", Theme.Heading, Theme.Accent), 16);
        Row(Theme.Wrapping(Theme.Label("Use your fork or a branch you can push to, then open a pull request on GitHub.", Theme.Body, Theme.Muted)));
        Row(Links(LinkButton("&Documentation", ProjectDestination.Documentation),
            LinkButton("Contributing &guide", ProjectDestination.Contributing),
            LinkButton("Developer set&up", ProjectDestination.Development),
            LinkButton("&Pull requests", ProjectDestination.PullRequests),
            LinkButton("&New pull request", ProjectDestination.NewPullRequest)));

        Row(Theme.Label("Selected link", Theme.Small, Theme.Muted), 12);
        _address.Name = "SelectedLink";
        _address.ReadOnly = true;
        _address.AccessibleName = "Selected link";
        _address.Dock = DockStyle.Fill;
        _copyLink.Name = "CopyLink";
        _copyLink.Enabled = false;
        _copyLink.Click += (_, _) => ShowResult(_actions.CopyLink());
        TableLayoutPanel addressRow = Theme.Grid(Theme.Fill, Theme.Auto);
        Theme.AddRow(addressRow, _address, _copyLink);
        Row(addressRow, 4);

        _message.Name = "HelpStatus";
        _message.AccessibleRole = AccessibleRole.Alert;
        Row(Theme.Wrapping(_message));
        _scroll.Controls.Add(root);
        Controls.Add(_scroll);
        Controls.Add(footer);
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
            bool opened = _actions.Open(destination, _context);
            _address.Text = _actions.TargetUrl;
            _copyLink.Enabled = _address.Text.Length > 0;
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
        _message.Text = _actions.Message;
        _message.ForeColor = success ? Theme.Accent : Theme.Warning;
        if (_message.Text.Length > 0)
        {
            _scroll.PerformLayout();
            _scroll.ScrollControlIntoView(_message);
        }
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
