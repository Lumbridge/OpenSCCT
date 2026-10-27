using System;
using Godot;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Chat;
using OpenScctGame.Controls;
using OpenScctGame.Hud;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Play;

/// <summary>
/// The In-Game Menu in the play scene (spec/hud/in-game-menu.md): the key the player's layout binds to
/// <c>ShowMenu</c> (Escape in both stock layouts) opens it; the player's controls stop and the pointer is freed
/// while it is open, and the match goes on behind it (the original never pauses); its keys and buttons close it,
/// open the options, send everyone back to the lobby (a network game's host) or leave the session or the game.
/// <code>
/// PlayMenu.Attach(this, Session, Controls, PlayHud.Attach(this, Session, Controls, Hud));
/// </code>
/// </summary>
public partial class PlayMenu : Node
{
    private CanvasLayer? _layer;
    private bool _inputWasEnabled = true;

    public PlayMenu()
    {
        Name = "PlayMenu";
    }

    public PlaySession? Session { get; set; }

    public PlayInput? PlayInput { get; set; }

    /// <summary>The chat line, the mission status page: while one of them has the keys, ShowMenu waits.</summary>
    public PlayHud.Parts? Hud { get; set; }

    public InGameMenu? Menu { get; private set; }

    public InGameMenuModel Model { get; } = new();

    public bool IsOpen => Model.IsOpen;

    /// <summary>Seconds on the menu's clock.</summary>
    public static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>Adds the menu to the play scene <paramref name="scene"/>.</summary>
    public static PlayMenu Attach(Node scene, PlaySession session, PlayInput? input, PlayHud.Parts? hud)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(session);
        var menu = new PlayMenu { Session = session, PlayInput = input, Hud = hud };
        scene.AddChild(menu);
        return menu;
    }

    public override void _Ready()
    {
        // Over the HUD and the status page, under the F12 options overlay.
        _layer = new CanvasLayer { Name = "InGameMenuLayer", Layer = HudLayer.DefaultLayer + 4 };
        Menu = new InGameMenu { Model = Model, Now = () => Now, GearSource = GearModel };
        Menu.Acted += Act;
        Menu.GearApplied += ApplyGear;
        _layer.AddChild(Menu);
        AddChild(_layer);
    }

    public override void _Input(InputEvent @event)
    {
        if (IsOpen)
        {
            MenuInput(@event);
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } && GodotKeys.ToIk(@event) is { } ik)
        {
            if (OpensMenu(ik))
            {
                Open();
                GetViewport().SetInputAsHandled();
            }
            else if (Runs(ik, GearCommand))
            {
                OpenGear();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>While the menu is open no key reaches the game: what the menu's pages did not use stops here.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen && @event is InputEventKey)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void MenuInput(InputEvent @event)
    {
        if (Model.Page == InGameMenuPage.Options)
        {
            // The options screen has the keys (its Escape and F12 come back here through its Closed).
            return;
        }

        bool press = @event is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true };
        if (!press || GodotKeys.ToIk(@event) is not { } ik)
        {
            return;
        }

        bool key = @event is InputEventKey;
        if (key && Model.Page == InGameMenuPage.Main && !Model.KeysLive(Now))
        {
            // The first half second takes every key.
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key || InGameMenuModel.IsCloseKey(ik))
        {
            InGameMenuAction action = Model.KeyPressed(ik, Now);
            if (action != InGameMenuAction.None || InGameMenuModel.IsCloseKey(ik))
            {
                GetViewport().SetInputAsHandled();
                Menu?.Sync();
                Act(action);
            }
        }
    }

    /// <summary>The gear picker's command (an OpenSCCT addition; InputBindings gives it a key when none has it).</summary>
    public const string GearCommand = "ChangeGear";

    /// <summary>The key runs <paramref name="command"/> in the player's layout and nothing else has the keys.</summary>
    private bool Runs(string ik, string command)
    {
        if (PlayInput is not { Enabled: true, Bindings: { } bindings } || Session is null || Session.IsStopped ||
            (Hud?.Chat?.IsOpen ?? false) || (Hud?.Status?.IsOpen ?? false))
        {
            return false;
        }

        foreach (KeyAction a in bindings.Actions(ik))
        {
            if (a.Kind == KeyActionKind.Command && string.Equals(a.Name, command, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The gear picker for the local player's side (spec/additions/gear-change.md).</summary>
    private GearMenuModel? GearModel() =>
        GearMenuModel.For(Session?.LocalController, SettingsStore.Current.Loadout,
            string.Equals(Session?.Settings.Map, OpenScct.Server.Hub.HubK.MapName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Apply: the choice is saved, then the server is asked to put it on (now or at the next spawn).</summary>
    private void ApplyGear(GearMenuModel model)
    {
        GameSettings settings = SettingsStore.Current;
        model.SaveTo(settings.Loadout);
        SettingsStore.Save(settings);
        Session?.LocalController?.ServerChangeGear();
    }

    /// <summary>The gear key: the menu opens on the gear picker, the controls stop as for ShowMenu.</summary>
    public void OpenGear()
    {
        if (Model.OpenGear(Context(), Now) != InGameMenuAction.Opened)
        {
            return;
        }

        StopControls();
    }

    /// <summary>The key runs ShowMenu in the player's layout (Escape when no key is bound to it) and nothing else has the keys.</summary>
    private bool OpensMenu(string ik)
    {
        if (PlayInput is not { Enabled: true, Bindings: { } bindings } || Session is null || Session.IsStopped ||
            (Hud?.Chat?.IsOpen ?? false) || (Hud?.Status?.IsOpen ?? false))
        {
            return false;
        }

        foreach (KeyAction a in bindings.Actions(ik))
        {
            if (a.Kind == KeyActionKind.Command && string.Equals(a.Name, InGameMenuK.ShowMenuCommand, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return HudCommands.KeyOf(bindings, InGameMenuK.ShowMenuCommand) is null && string.Equals(ik, "Escape", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Who the player is: offline the player runs the server; online the lobby says whether it hosts.</summary>
    public InGameMenuContext Context()
    {
        OnlineService? online = GetTree().Root.GetNodeOrNull<OnlineService>(OnlineService.NodeName);
        SPlayerController? pc = Session?.LocalController;
        string map = Session?.Settings.Map ?? "";

        // As GadgetWorld.CurLevelIsTutorial: the TME and TSP tutorial maps.
        bool tutorial = map.StartsWith("TME", StringComparison.OrdinalIgnoreCase) || map.StartsWith("TSP", StringComparison.OrdinalIgnoreCase);
        if (online?.Client is { } client)
        {
            bool host = (client.Lobby.Local?.IsHost ?? false) || (pc?.PlayerReplicationInfo?.bIAmServer ?? false);
            return new InGameMenuContext(host, InGameMenuNet.Lan, tutorial);
        }

        return new InGameMenuContext(IAmServer: true, InGameMenuNet.None, tutorial);
    }

    /// <summary>Opens the menu (<c>ShowMenu</c>): the controls stop (<c>StopAllControls</c>) and the pointer is freed.</summary>
    [Uc("SBase.SPlayerController.StopAllControls", Sha1 = "eb78c611ba24d5a2018262f20449cc8473a6e64e")]
    [PortOf("uc:SBase.SPlayerController:StopAllControls", Spec = "spec/hud/in-game-menu.md#rules")]
    public void Open()
    {
        if (Model.Open(Context(), Now) != InGameMenuAction.Opened)
        {
            return;
        }

        StopControls();
    }

    private void StopControls()
    {
        if (PlayInput is { } input)
        {
            _inputWasEnabled = input.Enabled;
            if (input.Target is { } target)
            {
                input.ClearHeld(target);
            }

            input.ReleasePointer();
            input.Enabled = false;
        }

        Menu?.Sync();
    }

    /// <summary>Closes every page and gives the keys back.</summary>
    public void Close() => Act(Model.CloseAll());

    public override void _Process(double delta)
    {
        if (!IsOpen)
        {
            return;
        }

        InGameMenuAction action = Model.Tick(Now);
        if (action != InGameMenuAction.None)
        {
            Menu?.Sync();
            Act(action);
        }
    }

    private void Act(InGameMenuAction action)
    {
        switch (action)
        {
            case InGameMenuAction.Resume:
                Resume();
                break;
            case InGameMenuAction.ReturnToLobby:
                Resume();
                GetTree().Root.GetNodeOrNull<OnlineService>(OnlineService.NodeName)?.Client?.BackToLobby();
                break;
            case InGameMenuAction.EndSessionForAll:
            case InGameMenuAction.LeaveSession:
                Leave();
                break;
            case InGameMenuAction.QuitGame:
                Leave();
                GetTree().Quit();
                break;
        }
    }

    private void Resume()
    {
        Menu?.Sync();
        if (PlayInput is not { } input)
        {
            return;
        }

        input.Enabled = _inputWasEnabled;
        if (input.Enabled && input.CapturePointer && DisplayServer.GetName() != "headless")
        {
            OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Captured;
        }
    }

    /// <summary>Back to the menus: online the visit ends (a host's server with it); offline the match stops and the main menu opens.</summary>
    private void Leave()
    {
        SceneTree tree = GetTree();
        OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Visible;
        if (tree.Root.GetNodeOrNull<OnlineService>(OnlineService.NodeName) is { Client: not null } online)
        {
            online.Leave();
            return;
        }

        Node? scene = GetParent();
        Session?.Stop();
        FrontEnd.ShowMainMenu(tree);
        if (scene is not null && GodotObject.IsInstanceValid(scene) && !scene.IsQueuedForDeletion())
        {
            scene.QueueFree();
        }
    }
}
