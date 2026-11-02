using System.Collections.Generic;
using System.Text;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class DoorPlanOwnershipTests
{
    [Fact]
    public void InvestigatingMercFacesAndUsesTheEscapeButtonInsteadOfWatchingTheSearchSpot()
    {
        var collision = Floor();
        collision.AddBoxHullCollider(ColliderKind.Mover, "Door", CollisionDataBuilder.WorldFlags,
            V(-300f, -8f, 0f), V(300f, 8f, 256f));
        PawnRig rig = Rig(collision, null, V(0f, -160f, 0f), PawnTeam.Merc);
        var mover = new Mover
        {
            bStatic = false,
            bNoDelete = false,
            InitialState = "TriggerToggle",
            Tag = "Exit",
            MoveTime = 1f,
        };
        mover.KeyPos[1] = V(0f, 0f, 320f);
        Mover door = rig.Level.SpawnActor(mover, default, default)!;
        SInterrupteur button = rig.Level.SpawnActor(new SInterrupteur
        {
            bStatic = false,
            bNoDelete = false,
            Event = "InteriorButton",
            UsableBy = Usable.U_SPAWNDEFENSE,
            UseRelativeLocation = V(40f, 0f, 0f),
            fInteractionRadius = 60f,
            fInteractionHeight = 120f,
            bInteractInFront = true,
        }, V(0f, -220f, 0f), new FRotator(0, 16384, 0))!;
        var group = new FEventGroup();
        group.EventGroup.Add(new FSuperEvent { Event = door.Tag, Delay = 10f });
        rig.Level.SpawnActor(new SMagicEvent
        {
            bStatic = false,
            bNoDelete = false,
            Tag = button.Event,
            Groups = new List<FEventGroup> { group },
        }, default, default);
        var presses = rig.Level.SpawnActor(new PressProbe { Tag = button.Event }, default, default)!;
        // A normal guard post beyond the exit remains a reason to leave after the investigation expires.
        rig.Level.SpawnActor(new SComputerObjectiveTrigger
        {
            bStatic = false,
            bNoDelete = false,
            bCollideActors = false,
            bBlockPlayers = false,
            TriggerName = "Outside",
            UseRelativeLocation = default,
        }, V(0f, 500f, 0f), default);
        rig.Collision!.BindLevel(rig.Level, id => id == "Door" ? door : null);
        AiWorld world = World(rig, V(-256f, -320f, -100f), V(256f, 640f, 400f));
        AiDifficulty difficulty = AiDifficulty.For(AiSkill.Normal) with
        {
            SightRange = 1f,
            HearingScale = 0f,
            GadgetUse = 0f,
            Teamwork = 0f,
            DecisionNoise = 0f,
        };
        var bot = new AiController(world, rig.Controller, difficulty, 0);
        bot.Tick();
        var spy = new SPawnAttaque();
        var trace = new StringBuilder();
        bool investigated = false;
        for (int frame = 0; frame < 20 * Sim.Hz && rig.Pawn.Location.Y < 100f; frame++)
        {
            // The search evidence is north of the door, while its reachable interior button faces south.
            bot.Memory.Report(spy, V(0f, 480f, 89f), world.Now, 0.9f, AiContactSource.Heard);
            bot.Tick();
            investigated |= bot.Brain!.Goal == MercAi.GInvestigate;
            AiInput input = bot.LastInput;
            rig.Step(new PlayerInputFrame
            {
                BT_Up = input.BT_Up,
                BT_Down = input.BT_Down,
                BT_Left = input.BT_Left,
                BT_Right = input.BT_Right,
                bUsing = input.bUsing,
                FastSpeed = input.FastSpeed,
                View = input.View,
            });
            if (frame % Sim.Hz == 0)
            {
                trace.AppendLine($"{world.Now:F2} {bot.Status} {bot.Brain!.Doors.Status} {rig.Controller.GetStateName()} at {rig.Pawn.Location}");
            }
        }

        Assert.True(investigated, trace.ToString());
        Assert.Equal(1, presses.Count);
        Assert.True(rig.Pawn.Location.Y > 100f, trace.ToString());
        Assert.Equal(1, door.KeyNum);
    }

    private sealed class PressProbe : Actor
    {
        public int Count { get; private set; }

        protected override void Trigger(Actor? Other, Pawn? EventInstigator) => Count++;
    }
}
