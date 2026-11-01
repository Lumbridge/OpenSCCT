using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Options;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class ConsoleLocationTests
{
    [Fact]
    public void ReportPreservesCoordinatesAndSeparatesPawnAndViewDirectionAcrossLocales()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            commaCulture.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = commaCulture;
            var pawn = new SPawnAttaque_1Mesh
            {
                Location = new FVector(-7320.671f, 3432.2717f, 1232.125f),
                Rotation = new FRotator(0, 16384, 0),
                Physics = EPhysics.PHYS_Ledge,
            };
            var controller = new SPlayerController { Rotation = new FRotator(8192, 32768, 0) };
            string report = PlayerLocationReport.Format("PolaD", pawn, controller);
            Assert.Contains("map=PolaD side=spy", report);
            Assert.Contains("physics=PHYS_Ledge", report);
            Assert.Contains("location_uu=(-7320.671,3432.2717,1232.125)", report);
            Assert.Contains("pawn_rotation_ue=(pitch=0,yaw=16384,roll=0)", report);
            Assert.Contains("view_rotation_ue=(pitch=8192,yaw=32768,roll=0)", report);
            string direction = report.Split("view_forward=")[1].Trim('(', ')');
            string[] parts = direction.Split(',');
            Assert.InRange(float.Parse(parts[0], CultureInfo.InvariantCulture), -0.708f, -0.706f);
            Assert.InRange(float.Parse(parts[2], CultureInfo.InvariantCulture), 0.706f, 0.708f);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void CopyIsExplicitAndWorksWithoutAServer()
    {
        var host = new Host { LocationReport = "map=PolaD\nlocation_uu=(1,2,3)" };
        var commands = new ConsoleCommands(host);
        Assert.Contains("location_uu=(1,2,3)", commands.Run("whereami"));
        Assert.Null(host.Copied);
        Assert.Contains("Location report copied to clipboard.", commands.Run("WHEREAMI COPY"));
        Assert.Equal(host.LocationReport, host.Copied);
        Assert.Equal(0, host.Sent);
    }

    [Fact]
    public void MenusAndInvalidArgumentsDoNotCopyStaleReports()
    {
        var host = new Host();
        var commands = new ConsoleCommands(host);
        Assert.Equal("whereami: no local player in a map", Assert.Single(commands.Run("whereami copy")));
        host.LocationReport = "new report";
        Assert.Equal("usage: whereami [copy]", Assert.Single(commands.Run("whereami unexpected")));
        Assert.Null(host.Copied);
    }

    private sealed class Host : IConsoleHost
    {
        public GameSettings Settings { get; } = new();
        public bool Online => false;
        public string? LocationReport { get; set; }
        public string? Copied { get; private set; }
        public int Sent { get; private set; }
        public void CopyText(string text) => Copied = text;
        public void SaveSettings(GameSettings settings, bool display) { }
        public void ServerCommand(string command) => Sent++;
        public void ServerLogin(string password) => Sent++;
        public void Chat(string text) => Sent++;
        public void Quit() { }
    }
}
