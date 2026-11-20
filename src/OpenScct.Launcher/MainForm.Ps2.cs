using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using OpenScct.Launcher.Ps2;

namespace OpenScct.Launcher.App;

internal sealed partial class MainForm
{
    private Ps2Edition? _ps2;
    private Ps2Edition Ps2 => _ps2 ??= Ps2Edition.Load(_layout.Root);
    private string Ps2Status => Ps2.Status;
    private string Ps2ActionText => Ps2.ActionText;

    private void Ps2Action()
    {
        switch (Ps2.NextAction)
        {
            case OpenScct.Launcher.Ps2.Ps2Action.ChooseDisc: PickPs2File("Choose your Chaos Theory PS2 disc", Ps2.WithDisc); break;
            case OpenScct.Launcher.Ps2.Ps2Action.ChooseEmulator: PickPs2Emulator(); break;
            case OpenScct.Launcher.Ps2.Ps2Action.ChooseMod: PickPs2Mod(); break;
            default:
                RunPs2Action(() => Process.Start(Ps2.CreateStartInfo())?.Dispose());
                break;
        }
    }

    private Control BuildPs2Setup()
    {
        TableLayoutPanel grid = Theme.Grid(Theme.Fill, Theme.Auto);
        AddPs2Row(grid, "Original disc", Ps2.DiscPath, "Choose ISO",
            () => PickPs2File("Choose your Chaos Theory PS2 disc", Ps2.WithDisc));
        AddPs2Row(grid, "PCSX2", Ps2.EmulatorPath, "Choose PCSX2", PickPs2Emulator);
        return grid;
    }

    private Control BuildPs2Mods()
    {
        TableLayoutPanel grid = Theme.Grid(Theme.Fill, Theme.Auto);
        AddPs2Row(grid, "Patched disc image", Ps2.ModPath ?? "Choose a separately patched ISO for your disc release.",
            "Choose modded ISO", PickPs2Mod);
        if (Ps2.ModPath != null)
        {
            Button original = Theme.QuietButton("Use original disc", onPanel: true);
            original.Click += (_, _) => RunPs2Action(() => SavePs2(Ps2.WithoutMod()));
            Theme.AddRow(grid, null, original);
        }
        return grid;
    }

    private static void AddPs2Row(TableLayoutPanel grid, string title, string? detail, string action, Action click)
    {
        TableLayoutPanel labels = Theme.Grid(Theme.Fill);
        labels.Margin = new Padding(0, 4, 12, 8);
        Theme.AddRow(labels, Theme.Label(title, Theme.Heading));
        Theme.AddRow(labels, Theme.Wrapping(Theme.Label(detail ?? "Choose a local file.", Theme.Small, Theme.Muted)));
        Button button = Theme.Button(action);
        button.Click += (_, _) => click();
        Theme.AddRow(grid, labels, button);
    }

    private void PickPs2Mod()
    {
        if (!File.Exists(Ps2.DiscPath))
        {
            MessageBox.Show(this, "Set up your original disc on the Games tab first.", "PlayStation 2");
            return;
        }
        PickPs2File("Choose a patched copy of your Chaos Theory PS2 disc", Ps2.WithMod);
    }

    private void PickPs2Emulator() => PickPs2File("Choose PCSX2", Ps2.WithEmulator, "PCSX2 executable (*.exe)|*.exe");

    private void PickPs2File(string title, Func<string, Ps2Edition> select, string filter = "PlayStation 2 disc image (*.iso)|*.iso")
    {
        using var picker = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, RestoreDirectory = true };
        if (picker.ShowDialog(this) == DialogResult.OK)
            RunPs2Action(() => SavePs2(select(picker.FileName)));
    }

    private void SavePs2(Ps2Edition edition)
    {
        edition.Save(_layout.Root);
        _ps2 = edition;
        _detailsKey = null;
        RefreshLocal();
    }

    private void RunPs2Action(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "PlayStation 2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
