using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

/// <summary>
/// "Import from Enhanced": the player picks any folder of an Enhanced SCCT Versus (Reloaded) install and its settings,
/// profile and key layouts (<see cref="EnhancedImport"/>) are staged in the draft, kept by Apply like any other edit.
/// Closing the picker without a folder offers the part that needs no install: the profile and key layouts the game
/// keeps in ProgramData.
/// </summary>
public partial class OptionsMenu
{
    /// <summary>Opens the system's folder picker for the install.</summary>
    private void PickEnhancedInstall()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        var dialog = new FileDialog
        {
            Name = "EnhancedImportDialog",
            Title = "Choose your Enhanced SCCT Versus folder",
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            CurrentDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory),
        };
        bool closed = false;
        bool Close()
        {
            if (closed) return false;
            closed = true;
            _dialogOpen = false;
            dialog.QueueFree();
            return true;
        }

        dialog.DirSelected += folder =>
        {
            if (Close()) ImportEnhanced(folder);
        };
        dialog.Canceled += () =>
        {
            if (Close()) OfferImportWithoutInstall();
        };
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(900, 600));
    }

    /// <summary>No folder was chosen: when this machine has an Enhanced profile, ask whether to take just that.</summary>
    private void OfferImportWithoutInstall()
    {
        if (Read(EnhancedImport.FindWithoutInstall, EnhancedImport.UsualProfileFolder) is not { } source) return;
        Confirm("No install folder chosen",
            $"Import only the key bindings, volumes and gear of the Enhanced profile \"{source.ProfileName}\" on this PC? " +
            "Sensitivities, display and voice settings are in the install folder and need it chosen.",
            () => Stage(source, EnhancedImport.UsualProfileFolder), "Import");
    }

    /// <summary>
    /// Stages everything found at <paramref name="folder"/> (any folder inside the install) in the draft. False, with
    /// the reason in the note, when nothing could be imported; the draft is then unchanged.
    /// </summary>
    public bool ImportEnhanced(string folder)
    {
        bool failed = false;
        if (Read(() => EnhancedImport.Find(folder), folder, () => failed = true) is { } source) return Stage(source, folder);
        if (!failed) SetNote($"Nothing to import: no Enhanced SCCT Versus install was found at {folder}. Choose the folder that holds its System folder.");
        return false;
    }

    /// <summary>The profile and key layouts in ProgramData alone, staged in the draft (no install needed).</summary>
    public bool ImportEnhancedWithoutInstall() =>
        Read(EnhancedImport.FindWithoutInstall, EnhancedImport.UsualProfileFolder) is { } source && Stage(source, EnhancedImport.UsualProfileFolder);

    private EnhancedImport? Read(Func<EnhancedImport?> find, string from, Action? failed = null)
    {
        try
        {
            return find();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            failed?.Invoke();
            SetNote("Could not import from " + from + ": " + e.Message);
            return null;
        }
    }

    private bool Stage(EnhancedImport source, string from)
    {
        GameSettings edit = _edit.Clone();
        List<string> report;
        KeyLayout? spyKeys;
        KeyLayout? mercKeys;
        try
        {
            report = source.Apply(edit);
            spyKeys = source.Keys(merc: false, edit.MouseWheel);
            mercKeys = source.Keys(merc: true, edit.MouseWheel);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetNote("Could not import from " + from + ": " + e.Message);
            return false;
        }

        _edit = edit;
        if (spyKeys is not null) _drafts[PlaySide.Spy].Draft!.Reset(spyKeys);
        if (mercKeys is not null) _drafts[PlaySide.Merc].Draft!.Reset(mercKeys);
        if (spyKeys is not null || mercKeys is not null)
            report.Add(source.SplitKeyboard ? "Spy and mercenary key layouts." : "Key layout (one for both roles).");
        RebuildPages();
        SetNote($"Imported from {source.Root ?? from}. {string.Join(" ", report)} Review the pages, then press Apply to keep it all.");
        return true;
    }

    private void SetNote(string text)
    {
        if (_note is not null) _note.Text = text;
    }
}
