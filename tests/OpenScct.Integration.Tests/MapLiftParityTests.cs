using System;
using System.IO;
using System.Linq;
using OpenScct.Content;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// Exercises the authored call switches, doors and destinations of every converted elevator. This deliberately
/// omits collision: the test checks map binding and device sequencing, not riding or encroachment geometry.
/// </summary>
public sealed class MapLiftParityTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryConvertedLiftCanReachEveryAuthoredFloorThroughItsCallSwitch()
    {
        string? repo = MapCatalog.FindRepoRoot(AppContext.BaseDirectory);
        string root = Path.Combine(repo ?? AppContext.BaseDirectory, ".content", "svmap");
        Assert.SkipWhen(!Directory.Exists(root), "Converted maps are not available in .content/svmap.");
        int mapCount = 0, liftCount = 0, routeCount = 0;
        foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            string gameplayPath = Path.Combine(directory, GameplayDoc.RelativePath);
            if (!File.Exists(gameplayPath)) continue;
            GameplayDoc gameplay = GameplayDoc.Load(gameplayPath);
            if (!gameplay.Actors.Any(a => a.Class == "SBase.SLift")) continue;

            var world = new World(1);
            DeviceNatives.Install(world.Level);
            var loader = new MapLoader(MapCatalog.CreateMapClasses());
            MapLoadResult loaded = loader.Load(world.Level, gameplay,
                ClassTable.Load(Path.Combine(directory, ClassTable.RelativePath)));
            int mapRoutes = 0;
            foreach (SLift lift in loaded.Actors.OfType<SLift>())
            {
                string id = gameplay.Map + "/" + lift.Tag;
                Assert.NotEmpty(lift.DoorsAndSwitches);
                foreach (SLiftDoor? door in lift.LiftDoors)
                {
                    Assert.NotNull(door);
                    Assert.Same(lift, door.Base);
                }

                int initial = lift.KeyNum;
                // OrphS starts one cabin at key 2 but only exposes calls for floors 0 and 1.
                // Visit every callable floor; return to the start only when it has an authored call switch.
                int[] destinations = Enumerable.Range(0, lift.DoorsAndSwitches.Count)
                    .Where(floor => floor != initial)
                    .Concat(initial < lift.DoorsAndSwitches.Count ? [initial] : Array.Empty<int>()).ToArray();
                foreach (int destination in destinations)
                {
                    FDoorsAndSwitchesForOneKey floor = lift.DoorsAndSwitches[destination];
                    Assert.True(floor.Switches.Count > 0, id + " has no switch for floor " + destination);
                    SInterrupteur button = Assert.IsType<SInterrupteur>(floor.Switches[0]);
                    Assert.Equal(lift.Tag, button.Event);
                    Assert.NotNull(button.TriggerInteraction);
                    Assert.True(button.InteractionObj!.bUsable, id + " call switch is disabled for floor " + destination);
                    // HandleEventInfo is the shared terminal action after immediate use, hacking, or use delay.
                    button.TriggerInteraction.HandleEventInfo();
                    for (int tick = 0; tick < 60 * Sim.Hz && !ReferenceEquals(lift.StateNode, ActorState.None); tick++)
                    {
                        world.Step();
                    }

                    Assert.True(ReferenceEquals(lift.StateNode, ActorState.None),
                        id + " stalled in " + lift.GetStateName() + " going to floor " + destination);
                    Assert.Equal(destination, lift.KeyNum);
                    Assert.Equal(lift.BasePos + lift.KeyPos[destination], lift.Location);
                    for (int floorIndex = 0; floorIndex < lift.DoorsAndSwitches.Count; floorIndex++)
                    {
                        foreach (SInterrupteur? call in lift.DoorsAndSwitches[floorIndex].Switches)
                        {
                            Assert.NotNull(call);
                            Assert.Equal(floorIndex != destination, call.InteractionObj!.bUsable);
                        }

                        foreach (OpenScct.Core.Engine.Mover? door in lift.DoorsAndSwitches[floorIndex].Doors)
                        {
                            Assert.NotNull(door);
                            Assert.Equal(floorIndex == destination ? 0 : 1, door.KeyNum);
                            Assert.Equal(door.KeyNum, door.PrevKeyNum);
                        }
                    }

                    mapRoutes++;
                }

                liftCount++;
            }

            output.WriteLine($"{gameplay.Map}: {mapRoutes} elevator routes completed");
            routeCount += mapRoutes;
            mapCount++;
        }

        Assert.True(liftCount > 0, "No authored elevators were found.");
        output.WriteLine($"Validated {routeCount} routes across {liftCount} elevators on {mapCount} maps.");
    }
}
