using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>The world's answer to "can the camera see this point", for relevancy (a BSP-only line check).</summary>
public interface INetLineOfSight
{
    /// <summary>True when world geometry (the level BSP only: no actors, movers or static meshes) blocks the line.</summary>
    bool IsBlockedByWorld(FVector start, FVector end);
}

/// <summary>Who looks for one replication pass of a connection (spec/native/net-relevancy.md, Data).</summary>
public readonly record struct NetViewer(Actor RealViewer, Actor Viewer, FVector Source);

/// <summary>
/// IsNetRelevantFor of the base class and every slice override (spec/native/net-relevancy.md rules 1 to 14), with
/// the class-to-test mapping of the game's vtables, plus stock UE2 APlayerController (relevant only to its own
/// connection) for the Engine class. Fields of SBase classes are read by name, since OpenScct.Net does not
/// reference the gameplay port. The pawn cache of rule 2 (one answer per pawn per pass and viewer pair) lives here.
/// </summary>
public sealed class NetRelevancy
{
    /// <summary>A relevancy test: the actor, the viewer, and this object for the base tests and the pawn cache.</summary>
    public delegate bool Test(NetRelevancy relevancy, Actor actor, in NetViewer viewer);

    private readonly Dictionary<string, Test> _tests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<UcClassInfo, Test> _resolved = new();
    private readonly Dictionary<int, bool> _pawnCache = new();
    private readonly PropertyReaders _readers;

    public NetRelevancy(PropertyReaders readers)
    {
        _readers = readers ?? throw new ArgumentNullException(nameof(readers));
        Register("Engine.Pawn", static (r, a, in v) => r.PawnRelevant(a, v));
        Register("SBase.SBasePawn", static (r, a, in v) => r.SBasePawnRelevant(a, v));
        Register("Engine.PlayerController", static (_, a, in v) => ReferenceEquals(a, v.RealViewer));
        Register("SBase.SPlayerController", static (_, a, in v) => ReferenceEquals(a, v.RealViewer));
        Register("SBase.SGamePlayObject", static (r, a, in v) => r.ActorRelevant(a, v, gameplayObject: true));
        Register("SBase.SBasicGamePlayObject", static (r, a, in v) => r.ActorRelevant(a, v, gameplayObject: false));
        Register("SBase.SObjectiveTrigger", static (r, a, in v) => r.ActorRelevant(a, v, gameplayObject: false));
        Register("SBase.SComputerObjectiveTrigger", static (r, a, in v) => r.ActorRelevant(a, v, gameplayObject: true));
        Register("SBase.SFlagDropZone", static (r, a, in v) => r.ActorRelevant(a, v, gameplayObject: true));
        Register("SBase.SBombTargetObjectiveTrigger", static (r, a, in v) => r.ActorRelevant(a, v, gameplayObject: true));
        Register("SBase.SVitre", static (r, a, in v) => r.ActorRelevant(a, v, gameplayObject: true));
        Register("SBase.SInteractObject", static (r, a, in v) => r.ActorRelevant(a, v, gameplayObject: false));
        Register("SBase.SFlag", static (r, a, in v) => r.FlagRelevant(a, v, disk: false));
        Register("SBase.SDisk", static (r, a, in v) => r.FlagRelevant(a, v, disk: true));
        Register("SBase.SInterrupteur", static (r, a, in v) => r.DistanceOnlyRelevant(a, v));
        Register("SBase.SAmmoStock", static (r, a, in v) => r.DistanceOnlyRelevant(a, v));
        Register("SBase.SInfirmary", static (r, a, in v) => r.DistanceOnlyRelevant(a, v));
        Register("SBase.STriggerLight", static (r, a, in v) => r.TriggerLightRelevant(a, v));
        Register("SBase.SLaserTrigger", static (r, a, in v) => r.LaserTriggerRelevant(a, v));
        Register("SBase.SInteractionManager", static (_, a, in v) => IsOwnedBy(a, v.RealViewer));
        Register("SBase.SPlayerCam", static (_, a, in v) => IsOwnedBy(a, v.RealViewer));
        Register("SBase.SLeaveInteraction", static (r, a, in v) => r.BasedInteractionRelevant(a, v));
        Register("SBase.SShareObjectInteraction", static (r, a, in v) => r.BasedInteractionRelevant(a, v));
        Register("SBase.SGadTorche", static (r, a, in v) => a.Instigator is { } i ? r.IsRelevant(i, v) : r.ActorRelevant(a, v, false));
    }

    /// <summary>BSP line checks; null treats every line as clear.</summary>
    public INetLineOfSight? LineOfSight { get; set; }

    /// <summary>Overrides (or adds) the test of <paramref name="classPath"/> and its subclasses.</summary>
    public void Register(string classPath, Test test)
    {
        ArgumentNullException.ThrowIfNull(test);
        _tests[classPath] = test;
        _resolved.Clear();
    }

    /// <summary>Starts a pass of one connection: the pawn cache only holds answers for one pass and viewer pair.</summary>
    public void BeginPass() => _pawnCache.Clear();

    /// <summary>The class's IsNetRelevantFor (the nearest registered test, else the AActor test).</summary>
    [UcNative("AActor::IsNetRelevantFor", Exe = 0x10a8f760, Editor = 0x11182c90)]
    public bool IsRelevant(Actor actor, in NetViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(actor);
        UcClassInfo cls = actor.GetUcClass();
        if (!_resolved.TryGetValue(cls, out Test? test))
        {
            test = static (r, a, in v) => r.ActorRelevant(a, v, gameplayObject: false);
            for (UcClassInfo? c = cls; c is not null; c = c.Super)
            {
                if (_tests.TryGetValue(c.Path, out Test? t))
                {
                    test = t;
                    break;
                }
            }

            _resolved[cls] = test;
        }

        return test(this, actor, viewer);
    }

    /// <summary>"Owned by X": X is in the actor's Owner chain, the actor itself included.</summary>
    public static bool IsOwnedBy(Actor actor, Actor? x)
    {
        if (x is null)
        {
            return false;
        }

        for (Actor? a = actor; a is not null; a = a.Owner)
        {
            if (ReferenceEquals(a, x))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Rule 1, the AActor test (rule 5 adds the bone-attached pickable object step for gameplay objects).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-relevancy.md rules 1 and 5")]
    public bool ActorRelevant(Actor a, in NetViewer v, bool gameplayObject)
    {
        if (a.bAlwaysRelevant || ReferenceEquals(a, v.Viewer) || ReferenceEquals(v.Viewer, a.Instigator) ||
            IsOwnedBy(a, v.Viewer) || IsOwnedBy(a, v.RealViewer))
        {
            return true;
        }

        if (AmbientSoundInRange(a, v))
        {
            return true;
        }

        if (a.Owner is { } owner && ReferenceEquals(a.Base, owner) && !Bool(a, "bOnlyOwnerSee"))
        {
            return IsRelevant(owner, v);
        }

        if (gameplayObject && Bool(a, "bPickable") && Int(a, "AttachmentBone") != 0 && a.Base is { } carrier && IsSkeletal(carrier))
        {
            return IsRelevant(carrier, v);
        }

        if ((a.bHidden || Bool(a, "bOnlyOwnerSee")) && !a.bBlockPlayers && !HasAmbientSound(a))
        {
            return false;
        }

        float r = a.CollisionRadius;
        if ((a.Location - v.Source).SizeSquared() - r * r < K.AlwaysRelevantDistance * K.AlwaysRelevantDistance)
        {
            return true;
        }

        return !Blocked(a.Location, v.Source);
    }

    /// <summary>Rule 2, the APawn test with its per-pass cache.</summary>
    [UcNative("APawn::IsNetRelevantFor", Editor = 0x11182f40)]
    public bool PawnRelevant(Actor a, in NetViewer v)
    {
        if (_pawnCache.TryGetValue(a.ActorId, out bool cached))
        {
            return cached;
        }

        bool relevant = PawnTest(a, v);
        _pawnCache[a.ActorId] = relevant;
        return relevant;
    }

    private bool PawnTest(Actor a, in NetViewer v)
    {
        if (a.bAlwaysRelevant || IsOwnedBy(a, v.Viewer) || IsOwnedBy(a, v.RealViewer) || ReferenceEquals(a, v.Viewer) ||
            ReferenceEquals(v.Viewer, a.Instigator))
        {
            return true;
        }

        if (AmbientSoundInRange(a, v))
        {
            return true;
        }

        if ((a.bHidden || Bool(a, "bOnlyOwnerSee")) && !a.bBlockPlayers && !HasAmbientSound(a))
        {
            return false;
        }

        if ((a.Location - v.Source).Size() - a.CollisionRadius < K.AlwaysRelevantDistance)
        {
            return true;
        }

        return !Blocked(a.Location + new FVector(0f, 0f, a.CollisionHeight), v.Source) || !Blocked(a.Location, v.Source);
    }

    /// <summary>Rule 3: SBasePawn, with grab, attack and terminate partners relevant as a pair.</summary>
    [UcNative("ASBasePawn::IsNetRelevantFor", Exe = 0x10ae41c0, Editor = 0x111fe9b0)]
    public bool SBasePawnRelevant(Actor a, in NetViewer v)
    {
        bool relevant = PawnRelevant(a, v);
        ReadOnlySpan<string> partners = ["m_PawnNoMoreGrabbed", "m_AttackTarget", "m_AttackedBy"];
        if (relevant)
        {
            foreach (string field in partners)
            {
                if (Object(a, field) is Actor partner)
                {
                    _pawnCache[partner.ActorId] = true;
                }
            }

            return true;
        }

        bool pair = false;
        foreach (string field in partners)
        {
            if (Object(a, field) is Actor partner && _pawnCache.TryGetValue(partner.ActorId, out bool p) && p)
            {
                pair = true;
                break;
            }
        }

        // The camera network test is an IsA (SCamNetwork or a subclass). Either way out, the pawn's own cache is
        // rewritten as relevant for this pass (game 0x10ae41c0 through the cache write 0x10a304a0).
        if (!pair && !(Object(a, "UsedObject") is Actor used && IsCamNetwork(used.GetUcClass()) && Bool(a, "bUsingCamNetwork")))
        {
            return false;
        }

        _pawnCache[a.ActorId] = true;
        return true;
    }

    private static bool IsCamNetwork(UcClassInfo cls)
    {
        for (UcClassInfo? c = cls; c is not null; c = c.Super)
        {
            if (c.Name == "SCamNetwork")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Rules 6 and 7: the disk and the flag follow their carrier.</summary>
    public bool FlagRelevant(Actor a, in NetViewer v, bool disk)
    {
        if (a.bAlwaysRelevant)
        {
            return true;
        }

        if (a.bHidden)
        {
            return false;
        }

        if (a.Base is Pawn carrier)
        {
            return disk && _pawnCache.TryGetValue(carrier.ActorId, out bool c) ? c : IsRelevant(carrier, v);
        }

        return true;
    }

    /// <summary>Rule 8: switches, ammo stocks and infirmaries by distance only.</summary>
    public bool DistanceOnlyRelevant(Actor a, in NetViewer v) =>
        a.bAlwaysRelevant || (!a.bHidden && (a.Location - v.Source).Size() < K.AlwaysRelevantDistance);

    /// <summary>
    /// SLaserTrigger (spec/devices/laser-trigger.md rule 14): always relevant ones are; a hidden (absent) tripwire is
    /// not; otherwise while the viewer's distance squared minus LaserLength squared is at most 2500 squared.
    /// </summary>
    [UcNative("ASLaserTrigger::IsNetRelevantFor", Exe = 0x10ab6d80)]
    [Provenance(ProvenanceKind.Decoded, "0x10ab6d80: bAlwaysRelevant, bHidden, then d^2 - LaserLength^2 <= 6250000")]
    [Provenance(ProvenanceKind.Estimated, "0x10ab6d80 is taken as the vtable's IsNetRelevantFor by its shape (three arguments, the viewer's location); the vtable slot was not read")]
    public bool LaserTriggerRelevant(Actor a, in NetViewer v)
    {
        if (a.bAlwaysRelevant)
        {
            return true;
        }

        if (a.bHidden)
        {
            return false;
        }

        float length = Int(a, "LaserLength");
        return (a.Location - v.Source).SizeSquared() - (length * length) <= LaserRelevantSquared;
    }

    private const float LaserRelevantSquared = 6250000f;

    /// <summary>Rule 9: trigger lights.</summary>
    public bool TriggerLightRelevant(Actor a, in NetViewer v)
    {
        if (a.bAlwaysRelevant || a.bNoDelete)
        {
            return true;
        }

        if (a.bHidden && !HasAmbientSound(a))
        {
            return false;
        }

        float d = (a.Location - v.Source).Size();
        if (d < Float(a, "DistAlwaysVisibleIn"))
        {
            return true;
        }

        return d < Float(a, "DistNeverVisibleOut") && !Blocked(a.Location, v.Source);
    }

    /// <summary>Rule 11: leave and share-object interactions follow their base.</summary>
    public bool BasedInteractionRelevant(Actor a, in NetViewer v)
    {
        if (IsOwnedBy(a, v.RealViewer))
        {
            return true;
        }

        if (a.Owner is null || !ReferenceEquals(a.Base, a.Owner))
        {
            return false;
        }

        return IsRelevant(a.Base, v);
    }

    private bool AmbientSoundInRange(Actor a, in NetViewer v) =>
        HasAmbientSound(a) && (a.Location - v.Viewer.Location).Size() < K.AmbientSoundRelevancyFactor * Float(a, "SoundRadius");

    private bool HasAmbientSound(Actor a) => Read(a, "AmbientSound").Kind != RepValueKind.None;

    private static bool IsSkeletal(Actor a) => a.DrawType == EDrawType.DT_Mesh;

    private bool Blocked(FVector from, FVector to) => LineOfSight?.IsBlockedByWorld(from, to) ?? false;

    private RepValue Read(Actor a, string name) => _readers.Find(a.GetUcClass(), name) is { } reader ? reader(a) : RepValue.None;

    private bool Bool(Actor a, string name) => Read(a, name).AsBool;

    private int Int(Actor a, string name) => Read(a, name).I;

    private float Float(Actor a, string name)
    {
        RepValue v = Read(a, name);
        return v.Kind == RepValueKind.Float ? v.F : v.I;
    }

    private UObject? Object(Actor a, string name) => Read(a, name).O as UObject;
}
