using System;
using System.Linq;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Every wearable finish through the real model/material preview, including fitted solid tints.</summary>
public sealed class FittedSkinsSmoke : SmokeTest
{
    public const string TestName = "fitted-skins";
    public override string Name => TestName;
    private readonly CosmeticItem[] _items = CosmeticCatalog.All.Where(i => i.Kind != CosmeticKind.Hue).ToArray();
    private CanvasLayer? _layer;
    private readonly CosmeticPreview[] _views = new CosmeticPreview[6];
    private readonly Label[] _labels = new Label[6];
    private int _offset, _frames, _page;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_layer is null)
        {
            context.Tree.Root.Size = new Vector2I(1280, 720);
            _layer = new CanvasLayer { Layer = 100 };
            context.Tree.Root.AddChild(_layer);
            _layer.AddChild(new ColorRect { Size = new Vector2(1280,720), Color = new Color(.035f,.045f,.065f) });
            for (int i = 0; i < 6; i++)
            {
                var position = new Vector2(10+(i%3)*420, 10+(i/3)*350);
                _views[i] = new CosmeticPreview(new Vector2(410,315)) { Position = position, Size = new Vector2(410,315) };
                _labels[i] = new Label { Position = position+new Vector2(4,320), Size = new Vector2(410,25) };
                _layer.AddChild(_views[i]); _layer.AddChild(_labels[i]);
            }
            ShowPage();
            return SmokeResult.Running;
        }
        if (++_frames < 16) return SmokeResult.Running;
        MenuPage.Shot(_layer, $"fitted-skins-{_page:D2}");
        for (int i = 0; i < Math.Min(6,_items.Length-_offset); i++)
        {
            bool masked = false;
            foreach (Node node in _views[i].FindChildren("*", "MeshInstance3D", true, false))
            {
                if (node is not MeshInstance3D { Mesh: { } mesh } instance) continue;
                for (int surface=0;surface<mesh.GetSurfaceCount();surface++)
                    if (instance.GetSurfaceOverrideMaterial(surface) is ShaderMaterial shader &&
                        shader.GetShaderParameter("svm_finish_mask").AsGodotObject() is Texture2D)
                        masked = true;
            }
            if (!masked) return SmokeResult.Fail($"{_items[_offset+i].Id}: no fitted material mask on the preview model");
        }
        _page++;
        _offset += 6; _frames = 0;
        if (_offset >= _items.Length)
        {
            _layer.QueueFree();
            return SmokeResult.Pass($"{_items.Length} unlockable finishes rendered with fitted material masks.");
        }
        ShowPage();
        return SmokeResult.Running;
    }

    private void ShowPage()
    {
        for (int i=0;i<6;i++)
        {
            bool visible = _offset+i < _items.Length;
            _views[i].Visible = _labels[i].Visible = visible;
            if (!visible) continue;
            _views[i].ShowItem(_items[_offset+i]);
            _labels[i].Text = _items[_offset+i].Id;
        }
    }
}
