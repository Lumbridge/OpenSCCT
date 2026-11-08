using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenScct.Content;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

public partial class ContentScreen
{
    public const string AnimationSelectionFile = "user://animation-pack.txt";
    private void AnimationPacks()
    {
        VBoxContainer card = CareerPanels.Card(Body, "AnimationPacks", "Animation packs");
        CareerPanels.Text(card, "Help", "Choose the pack used for new sessions. Every player and the host must use the same pack. Map-local animations take precedence.", dim: true, size: 14);
        var choices = new OptionButton { Name = "ActiveAnimationPack", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        card.AddChild(choices); choices.AddItem("Stock animations"); choices.SetItemMetadata(0, "");
        string library = ProjectSettings.GlobalizePath("user://animation-packs");
        string selected = System.Environment.GetEnvironmentVariable("SVM_ANIMATION_PACK") ?? "";
        if (Directory.Exists(library))
            foreach (string folder in Directory.EnumerateDirectories(library).Where(p => !Path.GetFileName(p).StartsWith('.')).Order(StringComparer.Ordinal))
                try
                {
                    AnimationBundle? bundle = AnimationBundle.Load(folder); if (bundle is null) continue;
                    choices.AddItem(bundle.Project.Name); int index = choices.ItemCount - 1; choices.SetItemMetadata(index, folder); if (folder == selected) choices.Selected = index;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { GD.PushWarning("Animation pack: " + ex.Message); }
        var status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; card.AddChild(status);
        choices.ItemSelected += index =>
        {
            OnlineService service = OnlineService.Get(GetTree());
            if (service.Client is not null || service.Hosting is not null) { status.Text = "Leave the current session before changing animation packs."; return; }
            string folder = choices.GetItemMetadata((int)index).AsString();
            System.Environment.SetEnvironmentVariable("SVM_ANIMATION_PACK", folder.Length == 0 ? null : folder);
            service.Catalog.Maps.AnimationPackFolder = folder.Length == 0 ? null : folder;
            File.WriteAllText(ProjectSettings.GlobalizePath(AnimationSelectionFile), folder);
            status.Text = "Selected for new sessions.";
        };
        var import = UiStyle.MenuButton("Import Animation Pack…", () =>
        {
            var dialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true, Filters = ["*.osanimpack;OpenSCCT Animation Pack"] };
            AddChild(dialog);
            dialog.FileSelected += async path =>
            {
                status.Text = "Importing animation pack…";
                try { string installed = await Task.Run(() => AnimationPackStore.Install(path, library)); status.Text = "Imported " + (AnimationBundle.Load(installed)?.Project.Name ?? "animation pack") + ". Reopen Maps & Content to select it."; }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { status.Text = ex.Message; }
                dialog.QueueFree();
            };
            dialog.Canceled += () => dialog.QueueFree(); dialog.PopupCentered(new(800, 600));
        }, "ImportAnimations");
        card.AddChild(import);
    }
}
