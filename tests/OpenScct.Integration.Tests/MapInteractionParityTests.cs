using System;
using System.IO;
using System.Linq;
using OpenScct.Content;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MapInteractionParityTests(ITestOutputHelper output)
{
    [Fact]
    public void AuthoredSwitchesCreateTheirInteractionsAndTeamManagersUpdateTheirActualTargets()
    {
        string? repo = MapCatalog.FindRepoRoot(AppContext.BaseDirectory);
        string root = Path.Combine(repo ?? AppContext.BaseDirectory, ".content", "svmap");
        Assert.SkipWhen(!Directory.Exists(root), "Converted maps are not available.");
        int switches = 0, hacks = 0, holds = 0, delayed = 0, managers = 0, securityLinks = 0;
        foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            string path = Path.Combine(directory, GameplayDoc.RelativePath);
            if (!File.Exists(path)) continue;
            GameplayDoc gameplay = GameplayDoc.Load(path);
            var world = new World(1);
            DeviceNatives.Install(world.Level);
            var loaded = new MapLoader(MapCatalog.CreateMapClasses()).Load(world.Level, gameplay,
                ClassTable.Load(Path.Combine(directory, ClassTable.RelativePath)));
            foreach (SInterrupteur panel in loaded.Actors.OfType<SInterrupteur>())
            {
                var interaction = Assert.IsType<STriggerInteraction>(panel.InteractionObj);
                Assert.Same(panel, interaction.Owner);
                Assert.Same(panel, interaction.OwnerInterrupteur);
                Assert.Equal(panel.iHackingTime != 0, interaction.bHackable);
                Assert.Equal(panel.bMaintainsSwitch, interaction.bMaintain);
                Assert.Equal(panel.bInitialyActive, interaction.bIsActive);
                Assert.Equal(panel.iReusableTime, interaction.iReusableTime);
                Assert.Equal(panel.bInteractInFront, interaction.bInteractInFront);
                Assert.Equal(panel.UsableBy == Usable.U_DEFAULT_USABILITY ? Usable.U_BOTH : panel.UsableBy,
                    interaction.UsableBy);
                Assert.True(Enum.IsDefined(panel.TriggerMethode), gameplay.Map + ": invalid switch trigger type");
                switches++;
                if (interaction.bHackable) hacks++;
                if (interaction.bMaintain) holds++;
                if (panel.iAttenteAvantActivation != 0) delayed++;
            }

            foreach (SInterrupteurManager manager in loaded.Actors.OfType<SInterrupteurManager>())
            {
                manager.eventTrigger(null, null);
                foreach (FInterrupteurValue target in manager.SInterrupteurList)
                {
                    Assert.NotNull(target.Interrupteur?.InteractionObj);
                    Assert.Equal(target.TriggeredUsability, target.Interrupteur!.InteractionObj!.UsableBy);
                }

                manager.eventUnTrigger(null, null);
                foreach (FInterrupteurValue target in manager.SInterrupteurList)
                {
                    Assert.Equal(target.UntriggeredUsability, target.Interrupteur!.InteractionObj!.UsableBy);
                }

                managers++;
            }

            foreach (SBasicGamePlayObject device in loaded.Actors.OfType<SBasicGamePlayObject>().Where(d => d.ERControl is not null))
            {
                Assert.Contains(device.ERControl, loaded.Actors);
                Assert.False(device.ERControl!.bDeleteMe);
                securityLinks++;
            }
        }

        Assert.True(switches > 0);
        output.WriteLine($"Validated {switches} switches ({hacks} hacks, {holds} holds, {delayed} delayed), " +
            $"{managers} team-switch managers and {securityLinks} security-zone links.");
    }
}
