using System;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>Optional canvas labels: no depth test, so teammates remain identifiable through walls.</summary>
public sealed class HudTeammateNames
{
    public bool ShowSpy { get; set; }
    public bool ShowMerc { get; set; }
    public MagmaDrawList DrawList { get; } = new();

    public void Draw(SPlayerController? pc, Func<FVector, Vector2?>? project, Vector2 screen)
    {
        DrawList.Clear();
        if (pc?.Pawn is not SBasePawn { Health: > 0, bDeleteMe: false } own ||
            pc.bGameEnded || HudK.IsSpectating(pc) || project is null) return;
        bool merc = own.SPawnType == OpenScct.Core.Engine.PawnType.SPAWNDEFENSE;
        if (!(merc ? ShowMerc : ShowSpy)) return;
        int index = 0;
        foreach (SBasePawn pawn in pc.XLevel.AllActors<SBasePawn>())
        {
            if (ReferenceEquals(pawn, own) || pawn.bDeleteMe || pawn.Health <= 0 ||
                pawn.SPawnType != own.SPawnType || pawn.PlayerReplicationInfo is not { bOnlySpectator: false } pri ||
                string.IsNullOrWhiteSpace(pri.PlayerName)) continue;
            FVector head = pawn.Location + new FVector(0, 0, pawn.CollisionHeight + 18f);
            if (project(head) is not { } point || point.X < 0 || point.X > screen.X || point.Y < 0 || point.Y > screen.Y) continue;
            // Keep these secondary to the crosshair and objective markers, even at close range.
            float distance = (pawn.Location - own.Location).Size() * HudK.DefaultDistanceScale;
            float fade = Math.Clamp((distance - 3f) / 57f, 0f, 1f);
            fade = fade * fade * (3f - 2f * fade);
            float height = (9f - 4f * fade) * screen.Y / MagmaRenderer.PageHeight;
            var colour = new Color(0.78f, 0.96f, 0.75f, 0.85f - 0.70f * fade);
            DrawList.Items.Add(HudText.Item("TeammateNames/" + index++, pri.PlayerName,
                new Rect2(point.X - screen.X / 2f, point.Y - height, screen.X, height), height, MagmaAlign.Center, colour));
        }
    }
}
