using System;
using System.Collections.Generic;
using OpenScct.Provenance;

namespace OpenScctGame.UI;

/// <summary>The player's network type as the menu sees it (<c>SPlayerController.NetType</c>).</summary>
public enum InGameMenuNet
{
    /// <summary>NET_NONE: offline play (the editor's Play Level, the main menu's offline match).</summary>
    None,

    /// <summary>NET_LAN: a network game. Reloaded lists internet games as LAN games, so every network game is one.</summary>
    Lan,
}

/// <summary>The buttons of the In-Game Menu (<c>SSMenu_LANMainPage</c>).</summary>
public enum InGameMenuButton
{
    Back,

    /// <summary>An OpenSCCT addition: the gear picker (spec/additions/gear-change.md).</summary>
    ChangeGear,
    StrategicMap,
    Options,
    ReturnToLobby,
    QuitSession,
}

/// <summary>What the menu shows.</summary>
public enum InGameMenuPage
{
    Closed,

    /// <summary>The In-Game Menu itself.</summary>
    Main,

    /// <summary>The options (<c>All_Settings</c>) over the game.</summary>
    Options,

    /// <summary>The gear picker (an OpenSCCT addition, spec/additions/gear-change.md).</summary>
    Gear,

    /// <summary><c>SSMenu_ConfirmQuitLAN</c>: End session, Cancel, Quit Game.</summary>
    ConfirmQuit,

    /// <summary><c>BackToLobbyValidation</c>: Return to lobby, Cancel.</summary>
    ConfirmLobby,

    /// <summary><c>QuitMatch_Page</c>: a client leaving ("Leaving session, please wait ...").</summary>
    Leaving,
}

/// <summary>What the play scene must do after a menu input.</summary>
public enum InGameMenuAction
{
    None,

    /// <summary>The menu opened: stop the player's controls and free the pointer.</summary>
    Opened,

    /// <summary>The menu closed: give the keys back and take the pointer again.</summary>
    Resume,

    /// <summary>The server ends the session for everyone (<c>ForceMenuQuit</c>): back to the menus.</summary>
    EndSessionForAll,

    /// <summary>A client leaves the session (<c>QuitMatch</c>): back to the menus.</summary>
    LeaveSession,

    /// <summary>The host sends everyone back to the lobby (<c>ForceMenuLobby</c>).</summary>
    ReturnToLobby,

    /// <summary>Quit Game: leave the session, then quit the program (<c>QuitGame</c>).</summary>
    QuitGame,
}

/// <summary>Who the player is when the menu opens.</summary>
/// <param name="IAmServer">The player runs the server (<c>PlayerReplicationInfo.bIAmServer</c>; offline play too).</param>
/// <param name="Net">The network type.</param>
/// <param name="Tutorial">The map is a tutorial map (<c>SPersistentData.CurLevelIsTutorial</c>).</param>
public readonly record struct InGameMenuContext(bool IAmServer, InGameMenuNet Net, bool Tutorial = false);

/// <summary>One shown button of the In-Game Menu: its caption and its top as a fraction of the screen height.</summary>
public readonly record struct InGameMenuEntry(InGameMenuButton Button, string Caption, float Top);

/// <summary>The menu's numbers and texts (spec/hud/in-game-menu.md, Constants).</summary>
public static class InGameMenuK
{
    [Provenance(ProvenanceKind.Decoded, "GUI.SSMenu_LANMainPage defaults TitlePage")]
    public const string Title = "In-Game Menu";

    [Provenance(ProvenanceKind.Decoded, "Default_kbd_0.ini, Default_kbd_1.ini: IK_Escape=ShowMenu")]
    public const string ShowMenuCommand = "ShowMenu";

    [Provenance(ProvenanceKind.Decoded, "GUI.SSMenu_LANMainPage state s_Init: Sleep(0.5), then bKeyevent = false")]
    public const double KeyIgnoreSeconds = 0.5;

    [Provenance(ProvenanceKind.Decoded, "GUI.QuitMatch_Page.InitComponent: SetTimer(0.6, false)")]
    public const double LeavingSeconds = 0.6;

    [Provenance(ProvenanceKind.Decoded, "GUI.SSMenu_LANMainPage defaults: WinWidth=0.5")]
    public const float ButtonWidth = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "GUI.GUIYesNoPage defaults BckGround: WinLeft 0.2, WinTop 0.3, WinWidth 0.6, WinHeight 0.4")]
    public const float DialogLeft = 0.2f, DialogTop = 0.3f, DialogWidth = 0.6f, DialogHeight = 0.4f;

    [Provenance(ProvenanceKind.Decoded, "GUI.GUIYesNoPage defaults Yes (WinLeft 0.35) and No (WinLeft 0.65), WinWidth 0.15, WinHeight 0.05")]
    public const float DialogYesLeft = 0.35f, DialogNoLeft = 0.65f, DialogButtonWidth = 0.15f, DialogButtonHeight = 0.05f;

    [Provenance(ProvenanceKind.Decoded, "GUI.GUIYesNoPage defaults: Yes and No at WinTop 0.65")]
    public const float DialogRow = 0.65f;

    [Provenance(ProvenanceKind.Decoded, "GUI.GUIYesNoQuitPage.InitComponent: Controls[2], Controls[3] WinTop 0.55; QuitGame at 0.65; LigneH at 0.623")]
    public const float QuitDialogRow = 0.55f, QuitDialogLine = 0.623f;

    [Provenance(ProvenanceKind.Decoded, "GUI.SSMenu_LANMainPage defaults: button captions")]
    public const string ChangeGear = "Change Gear";

    public const string Back = "Back", StrategicMap = "Strategic Map", Options = "Options", ReturnToLobby = "Return to Lobby", QuitSession = "Quit Session";

    [Provenance(ProvenanceKind.Decoded, "GUI.SSMenu_ConfirmQuitLAN defaults Txt_Label(0)")]
    public const string QuitQuestionServer = "Leaving the game now will end this session.|Are you sure?";

    [Provenance(ProvenanceKind.Decoded, "GUI.SSMenu_ConfirmQuitLAN defaults Txt_Label(1)")]
    public const string QuitQuestionClient = "Are you sure you wish|to quit ?";

    [Provenance(ProvenanceKind.Decoded, "GUI.GUIYesNoQuitPage defaults sBT_Yes, sBT_Cancel, QuitGame.Caption")]
    public const string EndSession = "End session", Cancel = "Cancel", QuitGame = "Quit Game";

    [Provenance(ProvenanceKind.Decoded, "GUI.BackToLobbyValidation defaults LabelCaption, sBT_Yes")]
    public const string LobbyQuestion = "Are you sure you wish|to return to lobby?", LobbyYes = "Return to lobby";

    [Provenance(ProvenanceKind.Decoded, "GUI.SSMenu_ConfirmQuitLAN defaults ClientQuitSession")]
    public const string LeavingText = "Leaving session, please wait ...";
}

/// <summary>
/// The In-Game Menu's rules (spec/hud/in-game-menu.md): which page <c>ShowMenu</c> opens, its buttons and where
/// they stand, the half second in which keys are ignored, the keys that close it, and the quit and lobby
/// confirmations. Godot-free: the play scene's <c>InGameMenu</c> draws it and <c>PlayMenu</c> carries out its
/// actions. Times are seconds of any clock the caller keeps.
/// </summary>
public sealed class InGameMenuModel
{
    private double _openedAt;
    private double _leavingAt;
    private InGameMenuAction _afterLeaving;
    private bool _gearOnly;

    public InGameMenuPage Page { get; private set; } = InGameMenuPage.Closed;

    public bool IsOpen => Page != InGameMenuPage.Closed;

    public InGameMenuContext Context { get; private set; }

    /// <summary>The shown buttons of the In-Game Menu for <see cref="Context"/>.</summary>
    public IReadOnlyList<InGameMenuEntry> Entries { get; private set; } = [];

    /// <summary>The question of the open confirmation page ("|" is a line break), or null.</summary>
    public string? Question => Page switch
    {
        InGameMenuPage.ConfirmQuit => Context.IAmServer ? InGameMenuK.QuitQuestionServer : InGameMenuK.QuitQuestionClient,
        InGameMenuPage.ConfirmLobby => InGameMenuK.LobbyQuestion,
        InGameMenuPage.Leaving => InGameMenuK.LeavingText,
        _ => null,
    };

    /// <summary>The strategic map page is not ported; its button is shown but cannot be used.</summary>
    public static bool StrategicMapAvailable => false;

    /// <summary>
    /// <c>SPlayerController.ShowMenu</c> (offline and LAN: <c>SSMenu_LANMainPage</c>): opens the menu. The caller
    /// stops the player's controls (<c>StopAllControls</c>) on <see cref="InGameMenuAction.Opened"/>.
    /// </summary>
    [Uc("SBase.SPlayerController.ShowMenu", Sha1 = "409339b53e0e3ff14019132bf2ed71acf57c3232")]
    [PortOf("uc:SBase.SPlayerController:ShowMenu", Spec = "spec/hud/in-game-menu.md#rules")]
    public InGameMenuAction Open(InGameMenuContext context, double now)
    {
        if (IsOpen)
        {
            return InGameMenuAction.None;
        }

        Context = context;
        Entries = Layout(context);
        Page = InGameMenuPage.Main;
        _openedAt = now;
        _gearOnly = false;
        return InGameMenuAction.Opened;
    }

    /// <summary>
    /// The gear key (an OpenSCCT addition, spec/additions/gear-change.md): opens the menu straight on the gear
    /// picker; leaving the picker closes the whole menu. Not on a tutorial map (its kit is fixed).
    /// </summary>
    public InGameMenuAction OpenGear(InGameMenuContext context, double now)
    {
        if (IsOpen || context.Tutorial)
        {
            return InGameMenuAction.None;
        }

        InGameMenuAction opened = Open(context, now);
        Page = InGameMenuPage.Gear;
        _gearOnly = true;
        return opened;
    }

    /// <summary>The gear picker's Back (or Escape): back to the In-Game Menu, or closed when the gear key opened it.</summary>
    public InGameMenuAction GearBack()
    {
        if (Page != InGameMenuPage.Gear)
        {
            return InGameMenuAction.None;
        }

        if (_gearOnly)
        {
            return CloseAll();
        }

        Page = InGameMenuPage.Main;
        return InGameMenuAction.None;
    }

    /// <summary>The gear picker's Apply: the choice is saved and sent, and the menu closes.</summary>
    public InGameMenuAction GearApplied() => Page == InGameMenuPage.Gear ? CloseAll() : InGameMenuAction.None;

    /// <summary><c>SSMenu_LANMainPage.InitComponent</c>: the shown buttons and their tops.</summary>
    [Uc("GUI.SSMenu_LANMainPage.InitComponent", Sha1 = "6abef97c2a8f3ea2ba4636884e5c8b5b23d75722")]
    [PortOf("uc:GUI.SSMenu_LANMainPage:InitComponent", Spec = "spec/hud/in-game-menu.md#rules")]
    [PortOf("def:GUI.SSMenu_LANMainPage", Spec = "spec/hud/in-game-menu.md#rules")]
    public static IReadOnlyList<InGameMenuEntry> Layout(InGameMenuContext context)
    {
        bool lobby = context.IAmServer && context.Net != InGameMenuNet.None;
        var list = new List<InGameMenuEntry>(6);
        if (lobby)
        {
            if (context.Tutorial)
            {
                list.Add(new(InGameMenuButton.Back, InGameMenuK.Back, 0.325f));
                list.Add(new(InGameMenuButton.Options, InGameMenuK.Options, 0.425f));
                list.Add(new(InGameMenuButton.ReturnToLobby, InGameMenuK.ReturnToLobby, 0.525f));
                list.Add(new(InGameMenuButton.QuitSession, InGameMenuK.QuitSession, 0.625f));
            }
            else
            {
                list.Add(new(InGameMenuButton.Back, InGameMenuK.Back, 0.275f));
                list.Add(new(InGameMenuButton.StrategicMap, InGameMenuK.StrategicMap, 0.375f));
                list.Add(new(InGameMenuButton.Options, InGameMenuK.Options, 0.475f));
                list.Add(new(InGameMenuButton.ReturnToLobby, InGameMenuK.ReturnToLobby, 0.575f));
                list.Add(new(InGameMenuButton.QuitSession, InGameMenuK.QuitSession, 0.675f));
            }
        }
        else if (context.Tutorial)
        {
            list.Add(new(InGameMenuButton.Back, InGameMenuK.Back, 0.325f));
            list.Add(new(InGameMenuButton.Options, InGameMenuK.Options, 0.475f));
            list.Add(new(InGameMenuButton.QuitSession, InGameMenuK.QuitSession, 0.625f));
        }
        else
        {
            list.Add(new(InGameMenuButton.Back, InGameMenuK.Back, 0.325f));
            list.Add(new(InGameMenuButton.StrategicMap, InGameMenuK.StrategicMap, 0.425f));
            list.Add(new(InGameMenuButton.Options, InGameMenuK.Options, 0.525f));
            list.Add(new(InGameMenuButton.QuitSession, InGameMenuK.QuitSession, 0.625f));
        }

        if (!context.Tutorial)
        {
            AddChangeGear(list);
        }

        return list;
    }

    /// <summary>
    /// An OpenSCCT addition: Change Gear goes under Back, and the buttons keep the original's 0.1 spacing, the column
    /// raised by half a step so it stays centred where it was.
    /// </summary>
    private static void AddChangeGear(List<InGameMenuEntry> list)
    {
        const float step = 0.1f;
        for (int i = 0; i < list.Count; i++)
        {
            list[i] = list[i] with { Top = list[i].Top - (step * 0.5f) + (i > 0 ? step : 0f) };
        }

        list.Insert(1, new(InGameMenuButton.ChangeGear, InGameMenuK.ChangeGear, list[0].Top + step));
    }

    /// <summary>True once the first half second after opening has passed (<c>s_Init</c>: bKeyevent false).</summary>
    [PortOf("uc:GUI.SSMenu_LANMainPage:s_Init@code", Spec = "spec/hud/in-game-menu.md#rules")]
    public bool KeysLive(double now) => now - _openedAt >= InGameMenuK.KeyIgnoreSeconds;

    /// <summary>The keys that close the menu on a press (IK names without the prefix; the pads are not mapped).</summary>
    public static bool IsCloseKey(string ik) =>
        string.Equals(ik, "Escape", StringComparison.OrdinalIgnoreCase) || string.Equals(ik, "RightMouse", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A key press while the menu is open. The menu takes every key (the caller lets none reach the game); this says
    /// what it does with it. On the In-Game Menu, after the first half second, Escape and the right mouse button
    /// close every page. On a confirmation page Escape cancels it (estimated: the GUI controller's own escape is not
    /// decoded).
    /// </summary>
    [Uc("GUI.SSMenu_LANMainPage.InternalOnKeyEvent", Sha1 = "d09c59f33d66b301003113ff94cd130a2684c26f")]
    [PortOf("uc:GUI.SSMenu_LANMainPage:InternalOnKeyEvent", Spec = "spec/hud/in-game-menu.md#rules")]
    public InGameMenuAction KeyPressed(string ik, double now)
    {
        ArgumentNullException.ThrowIfNull(ik);
        switch (Page)
        {
            case InGameMenuPage.Main when KeysLive(now) && IsCloseKey(ik):
                return CloseAll();
            case InGameMenuPage.Gear when string.Equals(ik, "Escape", StringComparison.OrdinalIgnoreCase):
                return GearBack();
            case InGameMenuPage.ConfirmQuit or InGameMenuPage.ConfirmLobby when string.Equals(ik, "Escape", StringComparison.OrdinalIgnoreCase):
                Page = InGameMenuPage.Main;
                return InGameMenuAction.None;
            default:
                return InGameMenuAction.None;
        }
    }

    /// <summary><c>SSMenu_LANMainPage.ButtonClick</c>: a button of the In-Game Menu.</summary>
    [Uc("GUI.SSMenu_LANMainPage.ButtonClick", Sha1 = "4ab90f9555138829c2605dd4badad8b83421d614")]
    [PortOf("uc:GUI.SSMenu_LANMainPage:ButtonClick", Spec = "spec/hud/in-game-menu.md#rules")]
    public InGameMenuAction Click(InGameMenuButton button)
    {
        if (Page != InGameMenuPage.Main || !Shown(button))
        {
            return InGameMenuAction.None;
        }

        switch (button)
        {
            case InGameMenuButton.Back:
                return CloseAll();
            case InGameMenuButton.Options:
                Page = InGameMenuPage.Options;
                return InGameMenuAction.None;
            case InGameMenuButton.ChangeGear:
                Page = InGameMenuPage.Gear;
                return InGameMenuAction.None;
            case InGameMenuButton.ReturnToLobby:
                Page = InGameMenuPage.ConfirmLobby;
                return InGameMenuAction.None;
            case InGameMenuButton.QuitSession:
                Page = InGameMenuPage.ConfirmQuit;
                return InGameMenuAction.None;
            default:
                // The strategic map page is not ported.
                return InGameMenuAction.None;
        }
    }

    /// <summary>The options page closed (Back, Save, Escape): back to the In-Game Menu.</summary>
    public void OptionsClosed()
    {
        if (Page == InGameMenuPage.Options)
        {
            Page = InGameMenuPage.Main;
        }
    }

    /// <summary><c>GUIYesNoPage.NoButtonClick</c>: Cancel on a confirmation page goes back to the In-Game Menu.</summary>
    [Uc("GUI.GUIYesNoPage.NoButtonClick", Sha1 = "d66b2798e1ca7bd958c09ae73c716dcb4d2d4340")]
    [PortOf("uc:GUI.GUIYesNoPage:NoButtonClick", Spec = "spec/hud/in-game-menu.md#rules")]
    public void Cancel()
    {
        if (Page is InGameMenuPage.ConfirmQuit or InGameMenuPage.ConfirmLobby)
        {
            Page = InGameMenuPage.Main;
        }
    }

    /// <summary>
    /// <c>SSMenu_ConfirmQuitLAN.YesButtonClick</c> (End session): the server ends the session for everyone at once; a
    /// client shows "Leaving session, please wait ..." and leaves <see cref="InGameMenuK.LeavingSeconds"/> later
    /// (<see cref="Tick"/> gives the action).
    /// </summary>
    [Uc("GUI.SSMenu_ConfirmQuitLAN.YesButtonClick", Sha1 = "faf28c260eaf35cf888bf0f3489d774a4b8afcca")]
    [Uc("GUI.QuitMatch_Page.Timer", Sha1 = "7bab7b43963b796cb1f48f5686721882564a2ff1")]
    [PortOf("uc:GUI.SSMenu_ConfirmQuitLAN:YesButtonClick", Spec = "spec/hud/in-game-menu.md#rules")]
    [PortOf("uc:GUI.QuitMatch_Page:Timer", Spec = "spec/hud/in-game-menu.md#rules")]
    public InGameMenuAction EndSession(double now)
    {
        if (Page != InGameMenuPage.ConfirmQuit)
        {
            return InGameMenuAction.None;
        }

        if (Context.IAmServer)
        {
            Page = InGameMenuPage.Closed;
            return InGameMenuAction.EndSessionForAll;
        }

        Page = InGameMenuPage.Leaving;
        _leavingAt = now + InGameMenuK.LeavingSeconds;
        _afterLeaving = InGameMenuAction.LeaveSession;
        return InGameMenuAction.None;
    }

    /// <summary><c>GUIYesNoQuitPage.QuitButtonClick</c> (Quit Game): the session ends (a server's sends its clients back first), then the program quits.</summary>
    [Uc("GUI.GUIYesNoQuitPage.QuitButtonClick", Sha1 = "e581978b39440345ca8d37f2c42bab14e178a0ad")]
    [PortOf("uc:GUI.GUIYesNoQuitPage:QuitButtonClick", Spec = "spec/hud/in-game-menu.md#rules")]
    public InGameMenuAction QuitGame()
    {
        if (Page != InGameMenuPage.ConfirmQuit)
        {
            return InGameMenuAction.None;
        }

        Page = InGameMenuPage.Closed;
        return InGameMenuAction.QuitGame;
    }

    /// <summary><c>BackToLobbyValidation.YesButtonClick</c>: the host sends everyone back to the lobby.</summary>
    [Uc("GUI.BackToLobbyValidation.YesButtonClick", Sha1 = "61214305dcb826c8044da47308d83f784d62a324")]
    [PortOf("uc:GUI.BackToLobbyValidation:YesButtonClick", Spec = "spec/hud/in-game-menu.md#rules")]
    public InGameMenuAction ConfirmLobby()
    {
        if (Page != InGameMenuPage.ConfirmLobby)
        {
            return InGameMenuAction.None;
        }

        Page = InGameMenuPage.Closed;
        return InGameMenuAction.ReturnToLobby;
    }

    /// <summary>Once a frame: the leaving page's timer.</summary>
    public InGameMenuAction Tick(double now)
    {
        if (Page == InGameMenuPage.Leaving && now >= _leavingAt)
        {
            Page = InGameMenuPage.Closed;
            return _afterLeaving;
        }

        return InGameMenuAction.None;
    }

    /// <summary>Closes every page (<c>Controller.CloseAll</c>), as when the round ends under the menu.</summary>
    public InGameMenuAction CloseAll()
    {
        if (!IsOpen)
        {
            return InGameMenuAction.None;
        }

        Page = InGameMenuPage.Closed;
        return InGameMenuAction.Resume;
    }

    private bool Shown(InGameMenuButton button)
    {
        foreach (InGameMenuEntry e in Entries)
        {
            if (e.Button == button)
            {
                return true;
            }
        }

        return false;
    }
}
