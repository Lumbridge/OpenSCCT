using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The replicated calls of the gadget ports, over the combat network (<see cref="ICombatNet"/>): an owner-to-server
/// call made on a machine without authority, and a server-to-owner call for a remote player. Each returns true when
/// the call left the machine (the caller then stops); otherwise the function runs in place.
/// </summary>
public static class GadgetNet
{
    /// <summary>
    /// An owner-to-server function of <paramref name="Self"/>: sent when this machine has no authority over it and a
    /// controlling player is known.
    /// </summary>
    public static bool ToServer(Actor Self, PlayerController? Sender, in CombatCall Call)
    {
        if (Self.Role >= ENetRole.ROLE_Authority || Sender is null)
        {
            return false;
        }

        CombatServices.Net(Self).ServerCall(Sender, Call);
        return true;
    }

    /// <summary>A server-to-owner function: sent when <paramref name="Owner"/> plays on another machine.</summary>
    public static bool ToOwner(PlayerController? Owner, in CombatCall Call)
    {
        if (Owner is not { IsRemotePlayer: true })
        {
            return false;
        }

        CombatServices.Net(Owner).ClientCall(Owner, Call);
        return true;
    }

    /// <summary>The player controlling <paramref name="Item"/>'s owner pawn (a gadget, a pawn), or null.</summary>
    public static SPlayerController? Controller(Actor? Item) => Item switch
    {
        SPlayerController pc => pc,
        Pawn { Controller: SPlayerController pc } => pc,
        { Owner: Pawn { Controller: SPlayerController pc } } => pc,
        { Instigator: { Controller: SPlayerController pc } } => pc,
        _ => null,
    };
}

/// <summary>
/// The SHud functions the gadgets call on their owner's machine. The HUD draws from these fields; the client HUD
/// (game/Scripts/Hud) owns the drawing.
/// </summary>
public static class GadgetHud
{
    /// <summary>
    /// SHud.AddMessageToAuxiliaryStack (the state message area): a text already there restarts when it had expired
    /// and stays time + 1 s; a new text is appended and only the newest entry is kept.
    /// </summary>
    [PortOf("uc:SBase.SHud:AddMessageToAuxiliaryStack", Spec = "spec/hud/messages.md#rules")]
    public static void AddMessageToAuxiliaryStack(SPlayerController? PC, string? Text, float Time)
    {
        if (PC?.myHUD is not SHud hud || string.IsNullOrEmpty(Text))
        {
            return;
        }

        int time = SvmMath.Trunc(Time);
        float now = hud.Level.TimeSeconds;
        List<FStackedMessage> stack = hud.AuxiliaryMessageStack;
        for (int i = 0; i < stack.Count; i++)
        {
            if (stack[i].Message == Text)
            {
                FStackedMessage m = stack[i];
                if (m.StartedTime + m.StayInStackTime <= now)
                {
                    m.StartedTime = now;
                }

                m.StayInStackTime = time + 1;
                stack[i] = m;
                return;
            }
        }

        stack.Add(new FStackedMessage { Message = Text, StartedTime = now, StayInStackTime = time });
        while (stack.Count > 1)
        {
            stack.RemoveAt(0);
        }

        CombatFx.Notify(PC, CombatFx.LocalMessage, Time);
    }

    /// <summary>
    /// SHud.AddMessageToStack (the main message area): a text already there stays max(time, MessageMustStayTime) from
    /// now; a new one is appended and past MaxStackedMessages the first entry of the lowest priority goes.
    /// </summary>
    [PortOf("uc:SBase.SHud:AddMessageToStack", Spec = "spec/hud/messages.md#rules")]
    public static void AddMessageToStack(SPlayerController? PC, string? Text, int Time, byte Prio = 0)
    {
        if (PC?.myHUD is not SHud hud || string.IsNullOrEmpty(Text))
        {
            return;
        }

        float now = hud.Level.TimeSeconds;
        int stay = SvmMath.Max(Time, SvmMath.Trunc(hud.MessageMustStayTime));
        List<FStackedMessage> stack = hud.MessageStack;
        for (int i = 0; i < stack.Count; i++)
        {
            if (stack[i].Message == Text)
            {
                stack[i] = stack[i] with { StayInStackTime = SvmMath.Trunc(now + stay - stack[i].StartedTime) };
                return;
            }
        }

        stack.Add(new FStackedMessage { Message = Text, StartedTime = now, StayInStackTime = stay, prio = Prio });
        if (hud.MaxStackedMessages > 0 && stack.Count > hud.MaxStackedMessages)
        {
            int lowest = 0;
            for (int i = 1; i < stack.Count; i++)
            {
                if (stack[i].prio < stack[lowest].prio)
                {
                    lowest = i;
                }
            }

            stack.RemoveAt(lowest);
        }
    }

    /// <summary>
    /// SHud.GotoState for a gadget view (s_Hud_UseObject, s_Hud_UseObject_StickyCam, s_Hud_UseObject_CamNetwork,
    /// s_H_Binoculars_Att, or None): the HUD enters the state when its port has it; the view name is raised for the
    /// client HUD either way.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The SHud states are drawing (HUD spec); the sim raises the name and enters a ported state only")]
    public static void SetHudState(SPlayerController PC, Name State)
    {
        if (PC.myHUD is SHud hud && (State.IsNone || hud.GetStateTable().Find(State) is not null))
        {
            hud.GotoState(State);
        }

        GadgetFx.Notify(PC, GadgetFx.HudView, State);
    }
}

/// <summary>Gadget view effects (SimEvents of kind Notify, read by the client's gadget HUD views).</summary>
public static class GadgetFx
{
    /// <summary>The HUD view changed: the state name in <see cref="SimEvent.Bone"/>.</summary>
    public static readonly Name HudView = new("GadgetHudView");

    /// <summary>A sticky cam stuck (A = the cam's actor id).</summary>
    public static readonly Name StickyCamSpawned = new("StickyCamSpawned");

    /// <summary>A sticky cam released gas.</summary>
    public static readonly Name StickyCamGas = new("StickyCamGas");

    /// <summary>A chaff grenade burst.</summary>
    public static readonly Name ChaffBurst = new("ChaffBurst");

    /// <summary>A mine was placed (A = the mine type).</summary>
    public static readonly Name MinePlaced = new("MinePlaced");

    /// <summary>The motion tracker locked a new target.</summary>
    public static readonly Name MotionLock = new("MotionLock");

    /// <summary>The spy binoculars marked or cleared an objective.</summary>
    public static readonly Name BinocularsMark = new("BinocularsMark");

    /// <summary>The spy binoculars' voice target changed (A = the merc's actor id, 0 when cleared).</summary>
    public static readonly Name BinocularsVoice = new("BinocularsVoice");

    /// <summary>A mine was removed or blew (A = 1 for an explosion).</summary>
    public static readonly Name MineGone = new("MineGone");

    /// <summary>Raises <paramref name="What"/> for <paramref name="Source"/>, with an optional name.</summary>
    public static void Notify(Actor? Source, Name What, Name Detail = default, float A = 0f, float B = 0f)
    {
        if (Source?.XLevel is not { } level)
        {
            return;
        }

        level.World.Events.Add(new SimEvent
        {
            Kind = SimEventKind.Notify,
            ActorId = Source.ActorId,
            What = What,
            Bone = Detail,
            Location = new FVector(A, B, 0f),
            Rotation = Source.Rotation,
        });
    }
}

/// <summary>Level queries the gadget natives share: the collision world, the pawn list, the actor list and sight lines.</summary>
public static class GadgetWorld
{
    /// <summary>The level's collision world, or null for a level without collision.</summary>
    public static ICollisionWorld? Collision(Actor Self) => TraceHitZones.CollisionOf(Self);

    /// <summary>
    /// The level's pawn list (ULevel + 0x5c in the game), in order. The port uses the level's actor order.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The game keeps its own pawn array (ULevel + 0x5c); its order is assumed to be the actor list order")]
    public static ActorIterator<SBasePawn> Pawns(Level Level) => Level.AllActors<SBasePawn>();

    /// <summary>
    /// A line from <paramref name="Start"/> to <paramref name="End"/> with <paramref name="Flags"/>; true and the hit
    /// when something was hit. Without collision nothing is hit.
    /// </summary>
    public static bool Line(Actor Source, FVector End, FVector Start, ETraceFlags Flags, out FCheckResult Hit)
    {
        if (Collision(Source) is not { } world)
        {
            Hit = FCheckResult.NoHit(End);
            return false;
        }

        return world.LineCheck(out Hit, Source, End, Start, FVector.Zero, Flags) && Hit.bHit;
    }

    /// <summary>
    /// The sight line of the gadget natives (flags 0x286): nothing hit, or only <paramref name="Target"/>. A world hit
    /// (the level's geometry, reported without an actor) blocks.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Chaff, mine and sticky gas scans: line flags 0x286, clear when it hits nothing or the target")]
    public static bool Sees(Actor Source, FVector From, Actor Target) =>
        !Line(Source, Target.Location, From, SightFlags, out FCheckResult hit) || ReferenceEquals(hit.Actor, Target);

    /// <summary>The actor a gadget line hit: the hit actor, or the level for a world hit (as the game reports it).</summary>
    public static Actor? HitActorOf(Actor Source, in FCheckResult Hit) => Hit.bHit ? Hit.Actor ?? Source.Level : null;

    /// <summary>0x286: movers, level, world geometry, stop at the first hit.</summary>
    public const ETraceFlags SightFlags = ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry |
        ETraceFlags.TRACE_StopAtFirstHit;

    /// <summary>The level's Use / interaction world-geometry test for an actor hit by a gadget trace.</summary>
    public static bool IsWorldGeometry(Actor? Hit) => Hit is null || Hit.bWorldGeometry || Hit is LevelInfo;

    /// <summary>A tutorial level (SPersistentData.CurLevelIsTutorial): the TME and TSP tutorial maps.</summary>
    [Provenance(ProvenanceKind.Estimated, "Tutorial maps by name prefix (TME, TSP); the persistent data tutorial list is not ported")]
    public static bool CurLevelIsTutorial(Actor Self)
    {
        string map = OpenScct.Game.GameFlow.GameFlowServices.Host(Self).GetURLMap() ?? string.Empty;
        return map.StartsWith("TME", System.StringComparison.OrdinalIgnoreCase) ||
            map.StartsWith("TSP", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A zero-extent line against one actor's collision cylinder (UPrimitive::LineCheck of a cylinder actor): true when
    /// the segment from <paramref name="Start"/> to <paramref name="End"/> crosses the upright cylinder.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 cylinder line check (the laser mine's CheckPresence tests the beam against the pawn's own primitive)")]
    public static bool SegmentHitsCylinder(FVector Start, FVector End, FVector Center, float Radius, float HalfHeight) =>
        SegmentCylinderHit(Start, End, Center, Radius, HalfHeight, out _);

    /// <summary>
    /// As <see cref="SegmentHitsCylinder"/>, with the entry point of the segment into the cylinder (the start when it
    /// starts inside).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 cylinder line check: the hit is the first point of the segment inside the cylinder")]
    public static bool SegmentCylinderHit(FVector Start, FVector End, FVector Center, float Radius, float HalfHeight, out FVector Hit)
    {
        Hit = End;
        FVector d = End - Start;
        float t0 = 0f;
        float t1 = 1f;

        // Height slab.
        float zMin = Center.Z - HalfHeight;
        float zMax = Center.Z + HalfHeight;
        if (d.Z == 0f)
        {
            if (Start.Z < zMin || Start.Z > zMax)
            {
                return false;
            }
        }
        else
        {
            float ta = (zMin - Start.Z) / d.Z;
            float tb = (zMax - Start.Z) / d.Z;
            if (ta > tb)
            {
                (ta, tb) = (tb, ta);
            }

            t0 = SvmMath.Max(t0, ta);
            t1 = SvmMath.Min(t1, tb);
            if (t0 > t1)
            {
                return false;
            }
        }

        // Circle in the horizontal plane.
        float ox = Start.X - Center.X;
        float oy = Start.Y - Center.Y;
        float a = d.X * d.X + d.Y * d.Y;
        float c = ox * ox + oy * oy - Radius * Radius;
        if (a == 0f)
        {
            if (c > 0f)
            {
                return false;
            }

            Hit = Start + d * t0;
            return true;
        }

        float b = 2f * (ox * d.X + oy * d.Y);
        float disc = b * b - 4f * a * c;
        if (disc < 0f)
        {
            return false;
        }

        float root = System.MathF.Sqrt(disc);
        float c0 = (-b - root) / (2f * a);
        float c1 = (-b + root) / (2f * a);
        float enter = SvmMath.Max(t0, c0);
        if (enter > SvmMath.Min(t1, c1))
        {
            return false;
        }

        Hit = Start + d * enter;
        return true;
    }

    /// <summary>The rotation of a direction vector (UE2 FVector::Rotation, roll 0).</summary>
    public static FRotator RotationOf(FVector Dir) => Dir.Rotation();
}
