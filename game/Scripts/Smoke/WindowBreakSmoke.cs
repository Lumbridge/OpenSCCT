using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics.Collision;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.Render.Golden;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Real compiled editor panes, replicated damage, shaped fragments, restored GPU lightmaps and vertex colours.</summary>
public sealed class WindowBreakSmoke : SmokeTest
{
    public const string TestName = "window-break";
    public override string Name => TestName;
    private sealed class Clock : IClock { public long NowMicroseconds { get; set; } }
    private readonly Clock clock = new();
    private PlaySession? session;
    private OffscreenView? view;
    private DeviceViews? devices;
    private int frames, phase, age;
    private string folder = "", shots = "";
    private readonly List<SVitre> authority = [];
    private readonly List<SVitre> local = [];
    private WindowLightTransfer? response;
    private readonly Dictionary<string, byte[]> initial = new();
    private double maxDrawMilliseconds;

    public override SmokeResult Step(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a renderer");
        if (session is null)
        {
            folder = System.Environment.GetEnvironmentVariable("SVM_WINDOW_TEST_MAP") ?? "";
            if (!Directory.Exists(folder)) return SmokeResult.Fail("SVM_WINDOW_TEST_MAP must name the compiled GlassLight test map");
            shots = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ?? Path.Combine("local", "window-break"); Directory.CreateDirectory(shots);
            session = new PlaySession(new PlaySettings { Map = Path.GetFileName(folder), MapRoots = [Path.GetDirectoryName(folder)!],
                Side = PlaySide.Spy, Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline, Bots = 0 }, clock);
        }
        clock.NowMicroseconds += 1_000_000 / Sim.Hz; session.Step(clock.NowMicroseconds); session.Input.EndTick();
        if (++frames > 1200) return Finish(false,"window test timed out");
        if (view is null)
        {
            if (session.Client.Map?.Loaded is not { } loaded || session.ServerController is null) return SmokeResult.Running;
            RenderMap map = MapBuilder.Build(folder);
            view = new OffscreenView(context.Tree.Root, map, new Vector2I(1280,720));
            devices = new DeviceViews(map,loaded,map.Root,map.SharedRoot);
            FVector eye = new(-1050,-1200,1050); view.View.SetUeCamera(eye,(-eye).Rotation(),90,cut:true);
            if (System.Environment.GetEnvironmentVariable("SVM_WINDOW_CLOSE_VIEW") == "1")
            { eye = new(-360,-850,480); view.View.SetUeCamera(eye,(new FVector(0,-600,150)-eye).Rotation(),75,cut:true); }
            foreach (SVitre pane in session.ServerController.XLevel.AllActors<SVitre>()) authority.Add(pane);
            foreach (SVitre pane in loaded.Level.AllActors<SVitre>()) local.Add(pane);
            if (authority.Count != 3 || local.Count != 3) return Finish(false,"three compiled panes did not load");
            using var stream=File.OpenRead(Path.Combine(folder,WindowLightTransfer.RelativePath)); response=WindowLightTransfer.Load(stream);
        }
        devices!.OnTick(session.Client.World.Level);
        var drawWatch = System.Diagnostics.Stopwatch.StartNew(); devices.Draw(1,1f/60);
        maxDrawMilliseconds = Math.Max(maxDrawMilliseconds, drawWatch.Elapsed.TotalMilliseconds);
        if (phase == 0 && ++age >= 20)
        {
            foreach (var batch in response!.Batches) foreach (var r in batch.Resources)
                if (r.Kind=="texture" && !initial.ContainsKey(r.Name) && view.Map.Textures.Get(r.Name,false)?.GetImage() is { } image)
                { image.Convert(Image.Format.Rgba8); initial[r.Name]=image.GetData(); image.Dispose(); }
            Shot("intact");
            foreach (SVitre pane in authority.GetRange(0,2))
                pane.eventTakeDamage(1,null,pane.Location+new FVector(18,12,0),new FVector(0,0,-200),SDamageType.SDTFlash);
            phase=1; age=0;
        }
        else if (phase == 1)
        {
            if (++age == 3)
            {
                SVitre pane = authority[2];
                pane.eventTakeDamage(1,null,pane.Location+new FVector(18,12,0),new FVector(0,0,-200),SDamageType.SDTFlash);
            }
            bool broken=true; foreach (SVitre pane in local) broken &= pane.DrawType==EDrawType.DT_None && pane.GlassImpactSerial==1;
            if (!broken) return SmokeResult.Running;
            if (devices.GlassBreakCount!=3 || devices.GlassFragmentCount<30) return Finish(false,"shaped pane fragments did not start");
            if (devices.WindowLightingUpdates<2) return SmokeResult.Running;
            int changed=0;
            foreach (var texture in initial)
            {
                using Image image=view.Map.Textures.Get(texture.Key,false)!.GetImage(); image.Convert(Image.Format.Rgba8);
                byte[] bytes=image.GetData(); for (int i=0;i<bytes.Length;i++) if (bytes[i]!=texture.Value[i]) changed++;
            }
            if (changed<100) return Finish(false,"GPU lightmaps still contain the intact-glass lighting");
            bool neutral=false;
            if (view.Map.Gameplay.TryGetValue("VertexReceiver",out var receiver) && receiver.Node.Mesh is ArrayMesh mesh)
                for (int s=0;s<mesh.GetSurfaceCount();s++) foreach (Color c in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Color].AsColorArray())
                    neutral |= c.R>.1f && MathF.Abs(c.R-c.G)<.01f && MathF.Abs(c.R-c.B)<.01f;
            if (!neutral) return Finish(false,"vertex-lit receiver kept its coloured-glass tint");
            var collision=session.Client.Map!.Physics!.Collision;
            foreach (SVitre pane in local)
            {
                collision.LineCheck(out FCheckResult hit,null,pane.Location+new FVector(0,0,-100),pane.Location+new FVector(0,0,100),default,ETraceFlags.TRACE_AllBlocking);
                if (hit.Time<1) return Finish(false,"broken pane still blocks its opening");
            }
            phase=2; age=0;
        }
        else if (phase == 2)
        {
            age++;
            if (age==1) Shot("fracture");
            if (age==8) Shot("falling");
            if (age==30) Shot("falling-later");
            if (age==100) Shot("floor-fragments");
            if (age>=285)
            {
                Shot("open-frame-restored-light");
                if (devices.GlassFragmentCount!=0) return Finish(false,"fragments did not expire");
                foreach (SVitre pane in authority) { pane.ResetDamageTime=1; pane.eventWaitReset(); }
                phase=3; age=0;
            }
        }
        else if (phase == 3)
        {
            bool intact=true; foreach (SVitre pane in local) intact &= pane.DrawType==EDrawType.DT_StaticMesh;
            if (!intact || devices.WindowLightingUpdates<3) return SmokeResult.Running;
            foreach (var texture in initial)
            {
                using Image image=view.Map.Textures.Get(texture.Key,false)!.GetImage(); image.Convert(Image.Format.Rgba8);
                if (!image.GetData().AsSpan().SequenceEqual(texture.Value)) return Finish(false,"reset did not restore the original lightmap");
            }
            if (++age<3) return SmokeResult.Running;
            Shot("reset-intact-light");
            return Finish(true,"3 replicated panes; shaped fragments, clear collision, restored surface/vertex lighting, reset; max view update ms="+maxDrawMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture));
        }
        return SmokeResult.Running;
    }
    private void Shot(string name) { using Image image=view!.Capture(); image.SavePng(Path.Combine(shots,name+".png")); }
    private SmokeResult Finish(bool ok,string message)
    { view?.Dispose(); view=null; session?.Dispose(); session=null; return ok ? SmokeResult.Pass(message) : SmokeResult.Fail(message); }
}
