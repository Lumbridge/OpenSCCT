using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// Traversal over the network as a player plays it offline (a listen server and the local client, inputs from the
/// keys' bindings, see <see cref="RubberBandRun"/>), at 0 ms and on SimLink C (120 ms, 20 ms jitter, 2% loss). Each
/// recorded scenario of tests/scenarios/traversal and tests/scenarios/moveset is played; the server must keep up
/// with the client, so the player is never pulled back:
/// <list type="bullet">
/// <item>no reinit (ReinitPawnBecauseOfDivergences, the teleport back),</item>
/// <item>no correction during the root motion of a mount, and none during a zip line ride on the loopback (on
/// SimLink C only ones under 3 uu, from moves lost or merged on the way),</item>
/// <item>every correction small: the decoded protocol makes a few (docs/NETCODE.md: a player who does not move sends
/// a move every other frame, and a state change can skip a frame's move, so the next move covers two or three frames
/// on the server); on SimLink C a drop from a ledge can lump more frames and the correction grows with the fall,</item>
/// <item>both copies end in the same state, within 2 uu.</item>
/// </list>
/// Needs the converted maps in <c>.content/svmap</c> (skipped otherwise).
/// </summary>
public sealed class RubberBandTraversalTests
{
    /// <summary>The largest correction the decoded protocol makes in these scenarios on the loopback (a frame of running, 7 uu, plus margin).</summary>
    private const float SmallCorrection = 16f;

    /// <summary>The same on SimLink C, where a ledge drop's first falling move can cover three or four frames.</summary>
    private const float SmallCorrectionOnALink = 32f;

    /// <summary>On SimLink C a ride may see corrections up to this size (a lost or late move), never a visible pull.</summary>
    private const float RideTolerance = 3f;

    /// <summary>Client and server agree this closely at the end of a scenario.</summary>
    private const float EndAgreement = 2f;

    private readonly ITestOutputHelper _output;

    public RubberBandTraversalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static SimLinkProfile? Link(string name) => name == "loopback" ? null : SimLinkProfile.Get(name);

    private RubberBandRun Play(string scenario, string link)
    {
        string map = RubberBandRun.MapOf(scenario);
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder(map) is null, $"{map} has not been converted into .content/svmap.");
        RubberBandRun run = RubberBandRun.Play(scenario, Link(link));
        _output.WriteLine(run.Describe());
        return run;
    }

    private static void AssertNoSnapBack(RubberBandRun run, string link)
    {
        Assert.True(run.Reinits.Count == 0, "the server reinitialised the pawn: " + run.Describe());
        Assert.True(run.LargestCorrection() <= (link == "loopback" ? SmallCorrection : SmallCorrectionOnALink),
            "a correction pulled the player back: " + run.Describe());
        Assert.True(run.FinalError <= EndAgreement, "client and server ended apart: " + run.Describe());
        Assert.True(run.Last.ClientState == run.Last.ServerState && run.Last.ClientPhysics == run.Last.ServerPhysics,
            "client and server ended in different states: " + run.Describe());
    }

    /// <summary>
    /// Ryan's report: after a ledge hang and the mount the player snaps back to where he was. The mount is root
    /// motion, for which the client sends no moves; the server must run it on its own (ASBasePawn::Tick), or its pawn
    /// stays under the ledge and the physics check reinitialises the client there after 3 s.
    /// </summary>
    [Theory]
    [InlineData("loopback")]
    [InlineData("C")]
    public void MountingFromALedgeHangDoesNotSnapBack(string link)
    {
        RubberBandRun run = Play("traversal/spy-ledge-pull-up", link);
        int mount = run.FirstFrame(EPhysics.PHYS_RootMotion);
        Assert.True(mount > 0, "the spy never started the mount: " + run.Describe());
        Assert.Empty(run.CorrectionsBetween(mount, run.Last.Frame));
        Assert.True(run.Last.Client.Z > -880f, "the spy is not on the container top: " + run.Describe());
        AssertNoSnapBack(run, link);
    }

    /// <summary>
    /// Ryan's report: on the zip line the player rubber-bands, sticks in a falling animation and teleports. The jump
    /// onto the line reaches the server as the jump action (SA_JUMP_ST_0H, physics Falling); without it the server's
    /// pawn stays walking, ignores every move of the jump and grab, and reinitialises the client.
    /// </summary>
    [Theory]
    [InlineData("loopback")]
    [InlineData("C")]
    public void TheZipLineRideHasNoCorrectionsAndEndsOnTheDeck(string link)
    {
        RubberBandRun run = Play("traversal/spy-zip-line-29", link);
        int grab = run.FirstFrame(EPhysics.PHYS_Linear);
        Assert.True(grab > 0, "the spy never caught the zip line: " + run.Describe());
        Assert.True(run.FirstFrame("s_ZipLine") > 0, "the spy never rode the line: " + run.Describe());
        int end = grab;
        while (end < run.Samples.Count && run.Samples[end].ClientPhysics == EPhysics.PHYS_Linear)
        {
            end++;
        }

        AssertRide(run, run.FirstFrame(EPhysics.PHYS_Falling), end, link);
        Assert.True(run.FirstFrame(EPhysics.PHYS_Ledge) > end, "the spy did not catch the deck ledge after the ride: " + run.Describe());
        AssertNoSnapBack(run, link);
    }

    /// <summary>Zip line GE 13 ridden down to the wall that ends it, then the fall.</summary>
    [Theory]
    [InlineData("loopback")]
    [InlineData("C")]
    public void TheZipLineToAWallHasNoCorrectionsOnTheWay(string link)
    {
        RubberBandRun run = Play("traversal/spy-zip-line-13", link);
        int grab = run.FirstFrame(EPhysics.PHYS_Linear);
        int wall = run.FirstFrame("s_ZipWall");
        Assert.True(grab > 0 && wall > grab, "the spy did not ride the line to the wall: " + run.Describe());
        AssertRide(run, run.FirstFrame(EPhysics.PHYS_Falling), wall, link);
        AssertNoSnapBack(run, link);
    }

    /// <summary>From the jump to the end of the line: no correction on the loopback, none over the tolerance on a link.</summary>
    private static void AssertRide(RubberBandRun run, int from, int to, string link)
    {
        foreach (RubberCorrection c in run.CorrectionsBetween(from, to))
        {
            Assert.True(link != "loopback" && c.Size <= RideTolerance,
                $"a correction of {c.Size:F1} uu at frame {c.Frame} during the ride (frames {from} to {to}): " + run.Describe());
        }
    }

    public static TheoryData<string, string, EPhysics> Moveset()
    {
        var data = new TheoryData<string, string, EPhysics>();
        var scenarios = new List<(string, EPhysics)>
        {
            ("traversal/spy-ledge-grab", EPhysics.PHYS_Ledge),
            ("traversal/spy-ledge-drop", EPhysics.PHYS_Ledge),
            ("traversal/spy-ledge-shimmy", EPhysics.PHYS_Ledge),
            ("traversal/spy-walkoff-ledge-catch", EPhysics.PHYS_Ledge),
            ("traversal/spy-pipe-climb-up", EPhysics.PHYS_OnPipe),
            ("traversal/spy-pipe-climb-down", EPhysics.PHYS_OnPipe),
            ("moveset/spy-ladder-up", EPhysics.PHYS_OnLadder),
            ("moveset/spy-ladder-down", EPhysics.PHYS_OnLadder),
            ("moveset/spy-fence-climb", EPhysics.PHYS_OnVerticalPlane),
            ("moveset/spy-hoh", EPhysics.PHYS_OnPipeH),
            ("moveset/spy-vault-barrier", EPhysics.PHYS_RootMotion),
            ("moveset/spy-wall-jump", EPhysics.PHYS_Falling),
        };
        foreach ((string scenario, EPhysics physics) in scenarios)
        {
            data.Add(scenario, "loopback", physics);
            data.Add(scenario, "C", physics);
        }

        return data;
    }

    /// <summary>Ledge grab, hang, shimmy and drop, pipe, ladder, fence, hand over hand, vault and wall jump.</summary>
    [Theory]
    [MemberData(nameof(Moveset))]
    public void TraversalPlaysWithoutSnapBack(string scenario, string link, EPhysics reaches)
    {
        RubberBandRun run = Play(scenario, link);
        Assert.True(run.FirstFrame(reaches) >= 0, $"the scenario never reached {reaches}: " + run.Describe());
        AssertNoSnapBack(run, link);
    }
}
