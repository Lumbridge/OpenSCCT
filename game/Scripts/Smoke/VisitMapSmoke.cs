using System.Linq;
using Godot;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Tutorials;
using OpenScctGame.Hud;
using OpenScctGame.Render;
using OpenScctGame.Render.Golden;

namespace OpenScctGame.Smoke;

public sealed class VisitMapSmoke : SmokeTest
{
    public const string TestName = "visit-map";
    public override string Name => TestName;
    private bool _started;
    private int _frames;
    private string? _message;
    private OffscreenView? _markerView;
    private int _markerFrames;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_markerView is not null)
        {
            if (++_markerFrames < 8) return SmokeResult.Running;
            using Image shot = _markerView.Capture();
            shot.SavePng(System.IO.Path.GetFullPath("../local/test-results/visit-map/direction-marker.png"));
            _markerView.Dispose();
            _markerView = null;
            return SmokeResult.Pass("authored help meshes have emissive textures; spy/merc information panels persist, update and clear; captured marker and HUD");
        }
        if (!_started)
        {
            _started = true;
            TutorialPlay.Visit(context.Tree, "AquaE", PlaySide.Spy);
            return SmokeResult.Running;
        }
        if (context.Tree.CurrentScene is not PlayRoot { Session.LocalController.Pawn: { } pawn } root ||
            root.Devices is null || ++_frames < 30) return SmokeResult.Running;
        if (_message is not null)
        {
            if (_frames < 45) return SmokeResult.Running;
            if (root.Hud?.ActivePage?.DrawList.Items.Any(i => i.Path.EndsWith("messageTuto") && i.Text == _message) != true)
                return SmokeResult.Fail("information text missing from rendered HUD draw list; snapshot=" + root.Hud?.Snapshot.Tutorial + "; node=" + root.Hud?.ActivePage?.Area("TutoMessage/Tuto")?.Frame + "; items=" + string.Join("|", root.Hud?.ActivePage?.DrawList.Items.Where(i => i.Path.Contains("Tuto")).Select(i => i.Path + ":" + i.Text) ?? []));
            // Exercise both teams for longer than the panel's closing animation, independent of render speed.
            foreach (HudPage page in new HudPage[] { new SpyHudPage(root.Hud!.Assets!), new MercHudPage(root.Hud.Assets!) })
            {
                var snapshot = new HudSnapshot { Valid = true, HasPawn = true, Tutorial = _message };
                for (int tick = 0; tick < 180; tick++) page.Update(snapshot, 1f / 60f);
                page.Draw(new Vector2(1280, 720), snapshot);
                if (!page.DrawList.Items.Any(i => i.Path.EndsWith("messageTuto") && i.Text == _message))
                    return SmokeResult.Fail("tutorial panel disappears after its opening animation: " + page.Side);
                snapshot.Tutorial = "A second information node.";
                page.Update(snapshot, 1f / 60f);
                page.Draw(new Vector2(1280, 720), snapshot);
                if (!page.DrawList.Items.Any(i => i.Text == snapshot.Tutorial)) return SmokeResult.Fail("next node text stale");
                snapshot.Tutorial = "";
                page.Update(snapshot, 1f / 60f);
                if (page.Area("TutoMessage")?.Visible == true) return SmokeResult.Fail("zone exit leaves panel open");
            }
            if (DisplayServer.GetName() != "headless")
            {
                string folder = System.IO.Path.GetFullPath("../local/test-results/visit-map");
                System.IO.Directory.CreateDirectory(folder);
                using Image shot = context.Tree.Root.GetTexture().GetImage();
                shot.SavePng(System.IO.Path.Combine(folder, "information-node.png"));
                RenderMap map = MapBuilder.Build(root.Map!.Folder);
                var marker = map.Gameplay["SHelpPath2083"].Node;
                foreach (Node node in map.Root.FindChildren("*", "GeometryInstance3D", true, false))
                    if (node is GeometryInstance3D geometry) geometry.Visible = ReferenceEquals(geometry, marker);
                marker.Visible = true;
                _markerView = new OffscreenView(context.Tree.Root, map, new Vector2I(640, 480));
                Vector3 center = marker.Transform * marker.Mesh!.GetAabb().GetCenter();
                var target = UeSpace.ToUe(center);
                var camera = target + new OpenScct.Core.FVector(0, 0, 350);
                _markerView.View.SetUeCamera(camera, (target - camera).Rotation(), 70f, cut: true);
                return SmokeResult.Running;
            }
            return SmokeResult.Pass("authored help meshes have emissive textures; information text reaches rendered HUD");
        }
        var level = root.Session!.Client.World.Level;
        var paths = new System.Collections.Generic.List<SHelpPath>();
        foreach (SHelpPath path in level.AllActors<SHelpPath>()) paths.Add(path);
        var meshes = root.Devices.Meshes.Where(m => m.Actor is SHelpPath).ToArray();
        GD.Print($"VISIT help={TutorialProfile.DisplayHelpInGame} paths={paths.Count} unhidden={paths.Count(p => !p.bHidden)} meshes={meshes.Length} visible={meshes.Count(m => m.Entry.Node.Visible)}");
        if (paths.Count == 0 || meshes.Length != paths.Count || paths.Any(p => p.bHidden) || meshes.Any(m => !m.Entry.Node.Visible))
            return SmokeResult.Fail("Visit a Map markers missing or hidden");
        foreach (var follower in meshes)
        {
            var mesh = follower.Entry.Node.Mesh!;
            for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                if (mesh.SurfaceGetMaterial(i) is not ShaderMaterial material ||
                    material.GetShaderParameter("tex_self_illum").AsGodotObject() is not Texture2D ||
                    material.GetShaderParameter("tex_self_illum_mask").AsGodotObject() is not Texture2D)
                    return SmokeResult.Fail("direction marker missing self-illumination texture: " + follower.Entry.Node.Name);
            }
        }
        SZoneTips? tip = null;
        foreach (SZoneTips candidate in level.AllActors<SZoneTips>())
            if (candidate.sMessage_ATT[0].Length > 0) { tip = candidate; break; }
        if (tip is null) return SmokeResult.Fail("No authored information nodes");
        tip.BeginTouch(pawn);
        var hud = (SHud)root.Session.LocalController!.myHUD!;
        if (hud.sTutoMap != tip.sMessage_ATT[0] + " " + tip.sMessage_ATT[1])
            return SmokeResult.Fail("authored information node did not reach HUD: " + hud.sTutoMap);
        _message = hud.sTutoMap;
        return SmokeResult.Running;
    }
}
