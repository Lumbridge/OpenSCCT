using System;
using System.Threading;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Options;
using OpenScctGame.Render;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>GPU loading/idle/reuse/off/teardown checks, independent of the optical image fixture.</summary>
public sealed class ResponsiveSmokePoolSmoke : SmokeTest
{
    public const string TestName = "responsive-smoke-pool";
    public override string Name => TestName;
    private readonly World _world = new(9981,ENetMode.NM_Client);
    private SubViewport? _viewport;
    private Camera3D? _camera;
    private SurfaceFogRegistry? _binding;
    private GameplayFxWarmup? _warmup;
    private GameReplicationInfo? _rules;
    private SSmokeEmitter? _emitter;
    private ResponsiveSmokeQuality _quality;
    private int _stage, _frames;
    private bool _warmed, _drained;
    private Counters _idle;
    private long _ordinaryBytes, _ordinaryFlowBytes;
    private SmokeResult? _result;
    private readonly record struct Counters(long Fields, long Prefix, long Flow, long PrefixDispatch, long FlowDispatch, long Growth);
    private static Counters Read() => new(ResponsiveSmokeRenderer.FieldAllocationCount,ResponsiveSmokeVolumeCache.AllocationCount,
        ResponsiveSmokeVisualFlow.AllocationCount,ResponsiveSmokeVolumeCache.Dispatches,ResponsiveSmokeVisualFlow.Dispatches,
        ResponsiveSmokeRenderer.GrowthUploadCount);
    private static bool HasStorage => ResponsiveSmokeRenderer.AllocatedFieldBytes > 0
        && ResponsiveSmokeVolumeCache.AllocatedBytes > 0 && ResponsiveSmokeVisualFlow.AllocatedBytes > 0;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_result is { } result) return result;
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("pool lifecycle validation requires a RenderingDevice");
        if (_viewport is null)
        {
            _quality = SettingsStore.Current.SmokeQuality; SettingsStore.Current.SmokeQuality = ResponsiveSmokeQuality.Medium;
            _rules = _world.Level.Spawn<GameReplicationInfo>()!; _rules.ResponsiveSmokeEnabled = true;
            _viewport = new SubViewport { Size = new(256,256), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            context.Tree.Root.AddChild(_viewport);
            _camera = new Camera3D { Current = true, Position = new(0,0,3), Far = 100 };
            _viewport.AddChild(_camera); _binding = new(_camera);
            ResponsiveSmokeRenderer.BindView(_binding.Slot,_camera,null,5,_world.Level);
            ResponsiveSmokeRenderer.CreateBackground(_camera);
            _warmup = GameplayFxWarmup.For(_viewport); context.Tree.Root.AddChild(_warmup);
            _warmup.PreloadResponsiveSmoke(_world.Level,null,_viewport);
            _warmup.Begin(() => _warmed = true);
            return SmokeResult.Running;
        }
        if (ResponsiveSmokeRenderer.VolumeError is { } error) return Finish(SmokeResult.Fail(error));
        if (_binding is not null) ResponsiveSmokeRenderer.BindView(_binding.Slot,_camera!,null,5,_world.Level);
        _frames++;
        switch (_stage)
        {
            case 0:
                if (!_warmed) { _frames = 0; break; }
                if (_frames < 4) break;
                if (!HasStorage || !ResponsiveSmokeRenderer.IsPrepared) return Finish(SmokeResult.Fail("loading warmup did not retain an enabled idle pool"));
                _ordinaryBytes = ResponsiveSmokeRenderer.AllocatedFieldBytes;
                _ordinaryFlowBytes = ResponsiveSmokeVisualFlow.AllocatedBytes;
                _idle = Read(); Next(); break;
            case 1:
                if (_frames < 8) break;
                if (Read() != _idle || ResponsiveSmokeRenderer.CloudsDrawn != 0 || ResponsiveSmokeRenderer.LastGrowthUploadBytes != 0)
                    return Finish(SmokeResult.Fail("enabled idle performed allocation, dispatch or growth upload"));
                Pop(); Next(); break;
            case 2:
            case 4:
                if (_frames < 6) break;
                Counters active = Read();
                if (active.Fields != _idle.Fields || active.Prefix != _idle.Prefix || active.Flow != _idle.Flow)
                    return Finish(SmokeResult.Fail($"stage={_stage}: an ordinary pop reallocated warmed textures"));
                if (active.PrefixDispatch <= _idle.PrefixDispatch || active.FlowDispatch <= _idle.FlowDispatch)
                    return Finish(SmokeResult.Fail("active smoke did not dispatch both GPU paths"));
                if (ResponsiveSmokeRenderer.LastGrowthUploadBytes != 0) return Finish(SmokeResult.Fail("mature smoke kept uploading growth"));
                if (_stage == 2) { _emitter!.Destroy(); Next(); }
                else { _emitter!.Destroy(); Pop(80); Next(); }
                break;
            case 3:
                if (_frames == 4)
                {
                    if (!HasStorage) return Finish(SmokeResult.Fail("expiry released the enabled pool"));
                    _idle = Read();
                }
                if (_frames < 12) break;
                if (Read() != _idle) return Finish(SmokeResult.Fail("expired smoke continued allocating or dispatching"));
                Pop(); Next(); break;
            case 5:
                if (_frames < 6) break;
                if (ResponsiveSmokeVisualFlow.AllocatedBytes <= _ordinaryFlowBytes+1024*1024 || ResponsiveSmokeRenderer.AllocatedFieldBytes <= _ordinaryBytes+4*1024*1024)
                    return Finish(SmokeResult.Fail("oversized packet did not exercise field and impulse overflow allocations"));
                _emitter!.Destroy(); Next(); break;
            case 6:
                if (_frames < 6) break;
                if (ResponsiveSmokeVisualFlow.AllocatedBytes != _ordinaryFlowBytes || ResponsiveSmokeRenderer.AllocatedFieldBytes != _ordinaryBytes)
                    return Finish(SmokeResult.Fail("oversized field or impulse capacity was not trimmed at idle"));
                _rules!.ResponsiveSmokeEnabled = false; Next(); break;
            case 7:
                if (_frames < 6) break;
                if (ResponsiveSmokeRenderer.AllocatedFieldBytes != 0 || ResponsiveSmokeVolumeCache.HasResources || ResponsiveSmokeVisualFlow.HasResources)
                    return Finish(SmokeResult.Fail("rule-off retained smoke storage or pipelines"));
                _rules!.ResponsiveSmokeEnabled = true; Pop(); Next(); break;
            case 8:
                if (_frames < 6) break;
                if (!HasStorage) return Finish(SmokeResult.Fail("re-enable failed to restore smoke resources"));
                _binding!.Dispose(); _binding = null;
                RenderingServer.CallOnRenderThread(Callable.From(() => Volatile.Write(ref _drained,true)));
                Next(); break;
            case 9:
                if (!Volatile.Read(ref _drained)) break;
                if (ResponsiveSmokeRenderer.AllocatedFieldBytes != 0 || ResponsiveSmokeVolumeCache.HasResources || ResponsiveSmokeVisualFlow.HasResources)
                    return Finish(SmokeResult.Fail("last-view teardown retained smoke resources"));
                return Finish(SmokeResult.Pass("loading warm dispatch; idle zero dispatch; first/repeat pop reuse; mature growth reuse; oversized field/impulse trim; rule-off and last-view full release"));
        }
        return SmokeResult.Running;
    }

    private void Pop(int blasts = 0)
    {
        FVector center = new(0,100,0);
        _emitter = _world.Level.Spawn<SSmokeEmitter>(center)!;
        var cells = new byte[ResponsiveSmokeField.CellCount]; Array.Fill(cells,(byte)1);
        var carves = new ResponsiveSmokeCarve[blasts];
        for (int i = 0; i < blasts; i++)
            carves[i] = new((uint)(i+1),ResponsiveSmokeCarveKind.Blast,center+new FVector(1000,0,0),center+new FVector(1000,0,0),1,1,2,.6f,1.4f,(byte[])cells.Clone());
        ResponsiveSmokeWorld smoke = ResponsiveSmokeWorld.For(_world.Level);
        smoke.ApplySnapshot(_emitter,new(1,0,2,center-new FVector(312.5f,312.5f,312.5f),center,cells,new ushort[cells.Length],carves));
        smoke.SynchronizeTime(2);
    }
    private void Next() { _stage++; _frames = 0; }
    private SmokeResult Finish(SmokeResult result)
    {
        _binding?.Dispose(); _binding = null;
        if (GodotObject.IsInstanceValid(_warmup)) _warmup!.QueueFree();
        _viewport?.QueueFree(); SettingsStore.Current.SmokeQuality = _quality;
        _result = result; return result;
    }
}
