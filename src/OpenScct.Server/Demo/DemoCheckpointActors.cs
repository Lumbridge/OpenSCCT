using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

public sealed partial class ServerHost
{
    internal bool WriteDemoCheckpointActors(NetConnection bootstrap)
    {
        if (bootstrap.ControlChannel is not { } control) return false;
        foreach (ActorChannel channel in bootstrap.ActorChannels)
        {
            string? state = channel.Actor switch
            {
                Mover mover => DemoCheckpointActor.Encode(channel.NetGuid, mover),
                SProjGrenade grenade => DemoCheckpointActor.Encode(channel.NetGuid, grenade),
                _ => null,
            };
            if (state is not null) control.Send(ControlMessage.TextLine(state));
        }
        return true;
    }
}

public sealed partial class ClientSession
{
    private bool ReceiveDemoCheckpointActor(string? text)
    {
        if (_transport is not DemoPlaybackTransport { IsBootstrapping: true } ||
            text?.StartsWith(DemoCheckpointActor.Prefix, StringComparison.Ordinal) != true)
            return false;

        DemoCheckpointActor.Apply(Driver, text.AsSpan(DemoCheckpointActor.Prefix.Length));
        return true;
    }
}

// Movers and grenades simulate from a starting pose on the client. Restore the current pose and remaining motion.
internal static class DemoCheckpointActor
{
    internal const string Prefix = "#demo-checkpoint-actor ";
    private const int MoverBytes = 75;
    private const int GrenadeBytes = 62;
    private const int MoverChars = ((MoverBytes + 2) / 3) * 4;
    private const int GrenadeChars = ((GrenadeBytes + 2) / 3) * 4;

    internal static string Encode(uint netGuid, Mover mover)
    {
        var w = new FBitWriter(MoverBytes * 8);
        w.WriteUInt32(netGuid);
        Write(w, mover.Location);
        Write(w, mover.Rotation);
        Write(w, mover.OldPos);
        Write(w, mover.OldRot);
        Write(w, mover.Velocity);
        w.WriteFloat(mover.PhysAlpha);
        w.WriteFloat(mover.PhysRate);
        w.WriteByte(mover.KeyNum);
        w.WriteByte(mover.PrevKeyNum);
        w.WriteByte(mover.bInterpolating && mover.Physics == EPhysics.PHYS_MovingBrush ? (byte)1 : (byte)0);
        return Prefix + Convert.ToBase64String(w.Data);
    }

    internal static string Encode(uint netGuid, SProjGrenade grenade)
    {
        var w = new FBitWriter(GrenadeBytes * 8);
        w.WriteUInt32(netGuid);
        Write(w, grenade.Location);
        Write(w, grenade.Rotation);
        Write(w, grenade.Velocity);
        w.WriteFloat(grenade.LifeSpan);
        w.WriteFloat(MathF.Max(0f, grenade.TimerRate - grenade.TimerCounter));
        w.WriteFloat(grenade.ProjectileSpeed);
        w.WriteFloat(grenade.fVitesseRotationPitch);
        w.WriteFloat(grenade.fVitesseRotationYaw);
        w.WriteByte((byte)grenade.Physics);
        w.WriteByte((byte)((grenade.bBounce ? 1 : 0) | (grenade.bAlreadyRebounded ? 2 : 0)));
        return Prefix + Convert.ToBase64String(w.Data);
    }

    internal static void Apply(NetDriver driver, ReadOnlySpan<char> encoded)
    {
        if (encoded.Length is not (MoverChars or GrenadeChars)) return;
        byte[] data = new byte[MoverBytes];
        if (!Convert.TryFromBase64Chars(encoded, data, out int count)) return;
        var r = new FBitReader(data, count * 8);
        switch (driver.FindActorByNetGuid(r.ReadUInt32()))
        {
            case Mover { bDeleteMe: false } mover when count == MoverBytes: Apply(r, mover); break;
            case SProjGrenade { bDeleteMe: false } grenade when count == GrenadeBytes: Apply(r, grenade); break;
        }
    }

    private static void Apply(FBitReader r, Mover mover)
    {
        FVector location = ReadVector(r);
        FRotator rotation = ReadRotation(r);
        FVector oldPos = ReadVector(r);
        FRotator oldRot = ReadRotation(r);
        FVector velocity = ReadVector(r);
        float alpha = r.ReadFloat();
        float rate = r.ReadFloat();
        byte key = r.ReadByte();
        byte prev = r.ReadByte();
        bool moving = r.ReadByte() == 1;
        if (!Finite(location) || !Finite(oldPos) || !Finite(velocity) ||
            !float.IsFinite(alpha) || alpha is < 0f or > 1f || !float.IsFinite(rate) || rate is < 0f or > 1000f ||
            key >= mover.KeyPos.Length || prev >= mover.KeyPos.Length)
            return;

        Place(mover, location, rotation, velocity);
        mover.OldPos = oldPos;
        mover.OldRot = oldRot;
        mover.PhysAlpha = alpha;
        mover.PhysRate = rate;
        mover.KeyNum = key;
        mover.PrevKeyNum = prev;
        mover.ClientUpdate = moving ? 1 : 0;
        mover.bInterpolating = moving;
        mover.SetPhysics(moving ? EPhysics.PHYS_MovingBrush : EPhysics.PHYS_None);
    }

    private static void Apply(FBitReader r, SProjGrenade grenade)
    {
        FVector location = ReadVector(r);
        FRotator rotation = ReadRotation(r);
        FVector velocity = ReadVector(r);
        float life = r.ReadFloat();
        float fuse = r.ReadFloat();
        float speed = r.ReadFloat();
        float pitchSpin = r.ReadFloat();
        float yawSpin = r.ReadFloat();
        var physics = (EPhysics)r.ReadByte();
        byte flags = r.ReadByte();
        if (!Finite(location) || !Finite(velocity) ||
            !float.IsFinite(life) || life < 0f || !float.IsFinite(fuse) || fuse < 0f ||
            !float.IsFinite(speed) || speed < 0f || !float.IsFinite(pitchSpin) || !float.IsFinite(yawSpin) ||
            physics is not (EPhysics.PHYS_None or EPhysics.PHYS_Falling or EPhysics.PHYS_Projectile))
            return;

        Place(grenade, location, rotation, velocity);
        grenade.SetPhysics(physics);
        grenade.LifeSpan = life;
        grenade.SetTimer(fuse, false);
        grenade.ProjectileSpeed = speed;
        grenade.fVitesseRotationPitch = pitchSpin;
        grenade.fVitesseRotationYaw = yawSpin;
        grenade.bBounce = (flags & 1) != 0;
        grenade.bAlreadyRebounded = (flags & 2) != 0;
    }

    private static void Place(Actor actor, FVector location, FRotator rotation, FVector velocity)
    {
        actor.Rotation = rotation;
        MovementNatives.FarMoveActor(actor, location);
        actor.Velocity = velocity;
    }

    private static void Write(FBitWriter w, FVector v) { w.WriteFloat(v.X); w.WriteFloat(v.Y); w.WriteFloat(v.Z); }
    private static void Write(FBitWriter w, FRotator r) { w.WriteInt32(r.Pitch); w.WriteInt32(r.Yaw); w.WriteInt32(r.Roll); }
    private static FVector ReadVector(FBitReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
    private static FRotator ReadRotation(FBitReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
    private static bool Finite(FVector v) =>
        MathF.Abs(v.X) <= 10_000_000f && MathF.Abs(v.Y) <= 10_000_000f && MathF.Abs(v.Z) <= 10_000_000f;
}
