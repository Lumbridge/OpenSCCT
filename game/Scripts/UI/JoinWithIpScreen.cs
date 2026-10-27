using System;
using System.Globalization;
using Godot;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>
/// Join with IP (the original's JoinWithIp page: "Server Address", "Server Port"; docs/MENUS.md). The port starts at
/// this game's port, 28888 (the original's was 8888); an address may carry its own port ("host:port"). The address is
/// kept for next time; the password, when the server needs one, is asked for here too.
/// </summary>
public partial class JoinWithIpScreen : MenuPage
{
    private Label? _status;

    public LineEdit? Address { get; private set; }

    public LineEdit? Port { get; private set; }

    public LineEdit? Password { get; private set; }

    public Button? JoinButton { get; private set; }

    public override void _Ready()
    {
        Name = "JoinWithIp";
        BuildFrame("Join with IP", 560f);
        OnlineSettings s = OnlineSettings.Current;
        (string host, int port) = Split(s.LastAddress);
        VBoxContainer box = Panel(Body, "Fields", null);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        box.AddChild(grid);
        grid.AddChild(new Label { Text = "Server Address" });
        Address = new LineEdit { Name = "Address", Text = host, PlaceholderText = "address or address:port", CustomMinimumSize = new Vector2(300f, 0f) };
        grid.AddChild(Address);
        grid.AddChild(new Label { Text = "Server Port" });
        Port = new LineEdit { Name = "Port", Text = port.ToString(CultureInfo.InvariantCulture), MaxLength = 5, CustomMinimumSize = new Vector2(120f, 0f) };
        grid.AddChild(Port);
        grid.AddChild(new Label { Text = "Password" });
        Password = new LineEdit { Name = "Password", Secret = true, PlaceholderText = "if the server has one", MaxLength = LobbySetup.MaxPasswordLength };
        grid.AddChild(Password);
        Address.TextSubmitted += _ => Join();
        Port.TextSubmitted += _ => Join();
        _status = new Label { Name = "Status", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Body.AddChild(_status);
        PageButton("Cancel", () => FrontEnd.ShowMainMenu(GetTree()), "Cancel");
        JoinButton = PageButton("Join", Join, "JoinIp");
        Address.GrabFocus();
    }

    /// <summary>"host:port" into its parts (the game's port when there is none).</summary>
    public static (string Host, int Port) Split(string? address)
    {
        string text = (address ?? string.Empty).Trim();
        int colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon && int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port))
        {
            return (text[..colon], port);
        }

        return (text, BuildInfo.DefaultGamePort);
    }

    private void Join()
    {
        string host = Address?.Text.Trim() ?? string.Empty;
        if (host.Length == 0)
        {
            ShowStatus("Enter the host's address.");
            return;
        }

        // A port in the address wins; otherwise the Server Port field's.
        (string h, int p) = Split(host);
        bool ownPort = h.Length != host.Length;
        if (!ownPort && int.TryParse(Port?.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int typed))
        {
            p = typed;
        }

        host = h;

        if (p is <= 0 or > 65535)
        {
            ShowStatus("The port is a number from 1 to 65535.");
            return;
        }

        string address = host + ":" + p.ToString(CultureInfo.InvariantCulture);
        OnlineSettings.Current.LastAddress = address;
        OnlineSettings.Current.Save();
        try
        {
            OnlineService.Get(GetTree()).Join(JoinTarget.FromAddress(address), Password?.Text.Length > 0 ? Password.Text : null);
        }
        catch (ArgumentException ex)
        {
            ShowStatus(ex.Message);
        }
    }

    private void ShowStatus(string text)
    {
        if (_status is not null)
        {
            _status.Text = text;
        }
    }
}
