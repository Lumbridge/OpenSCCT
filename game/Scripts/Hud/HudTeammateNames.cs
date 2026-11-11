using System;
using System.Globalization;
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
    public bool ShowHealth { get; set; }
    public float Opacity { get; set; } = 0.5f;
    public MagmaDrawList DrawList { get; } = new();

    public void Draw(SPlayerController? pc, Func<FVector, Vector2?>? project, Vector2 screen)
    {
        DrawList.Clear();
        if (pc?.Pawn is not SBasePawn { Health: > 0, bDeleteMe: false } own ||
            pc.bGameEnded || HudK.IsSpectating(pc) || project is null) return;
        // Merc Deathmatch uses the same pawn type for every opponent; it has no friendly labels.
        if (pc.IsMercDeathMatch) return;
        bool merc = own.SPawnType == OpenScct.Core.Engine.PawnType.SPAWNDEFENSE;
        if (!(merc ? ShowMerc : ShowSpy) || Opacity <= 0f) return;
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
            var colour = new Color(0.78f, 0.96f, 0.75f, (0.85f - 0.70f * fade) * Math.Clamp(Opacity, 0f, 1f));
            // Health already replicates from the authority with the pawn; do not infer a maximum or percentage.
            string text = ShowHealth ? string.Create(CultureInfo.InvariantCulture, $"{pri.PlayerName} ({pawn.Health} HP)") : pri.PlayerName;
            string path = "TeammateNames/" + index++;
            DrawList.Items.Add(HudText.Item(path, text,
                new Rect2(point.X - screen.X / 2f, point.Y - height, screen.X, height), height, MagmaAlign.Center, colour));
            if (!merc) continue;
            bool backpack = HasBackpack(pc, pawn);
            if (pawn.bSleeping || backpack)
            {
                float size = Mathf.Round(height * 1.75f);
                float gap = 2f * screen.Y / MagmaRenderer.PageHeight;
                float width = pawn.bSleeping && backpack ? size * 2f + gap : size;
                var rect = new Rect2(Mathf.Round(point.X - width / 2f),
                    Mathf.Round(point.Y - height - gap - size), size, size);
                // Sleeping replicates with the pawn, so the marker clears on waking or revival without a HUD timer.
                if (pawn.bSleeping)
                {
                    Icon(path + "/Sleep", "zzz", rect, new Color(0.55f, 0.77f, 1f, colour.A));
                    rect.Position += new Vector2(size + gap, 0f);
                }
                if (backpack)
                {
                    // Each viewer has a separate share. A full inventory does not mean their share is exhausted.
                    Color backpackColour = HasBackpackSupplies(pawn, own) ? colour : new Color(0.3f, 0.3f, 0.3f, colour.A);
                    Icon(path + "/Backpack", "backpack", rect, backpackColour);
                }
            }
        }
    }

    private static bool HasBackpack(SPlayerController pc, SBasePawn pawn)
    {
        // Other players' gadget inventories are owner-only; the carried mesh replicates its carrier to everyone.
        foreach (SBackPack backpack in pc.XLevel.AllActors<SBackPack>())
            if (ReferenceEquals(backpack.Instigator, pawn)) return true;
        return false;
    }

    private static bool HasBackpackSupplies(SBasePawn carrier, SBasePawn viewer) =>
        (ReferenceEquals(carrier.aBackPackOwner_A, viewer) &&
            (carrier.aNbChargesGad_Mate_A0 > 0 || carrier.aNbChargesGad_Mate_A1 > 0 ||
             carrier.aNbChargesGad_Mate_A2 > 0 || carrier.aNbChargesGad_Mate_A3 > 0)) ||
        (ReferenceEquals(carrier.aBackPackOwner_B, viewer) &&
            (carrier.aNbChargesGad_Mate_B0 > 0 || carrier.aNbChargesGad_Mate_B1 > 0 ||
             carrier.aNbChargesGad_Mate_B2 > 0 || carrier.aNbChargesGad_Mate_B3 > 0));

    private void Icon(string path, string name, Rect2 rect, Color colour)
    {
        if (HudActivityIcons.Load(name) is not { } texture) return;
        MagmaDrawItem icon = DrawList.Add(MagmaWidgetKind.Image);
        icon.Path = path;
        icon.NativeTexture = texture;
        icon.Clamp = true;
        icon.SetGeometry([rect.Position, new Vector2(rect.End.X, rect.Position.Y), rect.End, new Vector2(rect.Position.X, rect.End.Y)],
            [Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down], [colour, colour, colour, colour]);
    }
}
