using OpenScctGame.Views;
using Xunit;

namespace OpenScctGame.Logic.Tests.Views;

/// <summary>
/// spec/devices/vitre.md rule 2 in the views: a gameplay mesh node follows its actor's StaticMesh, including a change
/// to None (a broken pane), which hides the node. The node used to keep the pane drawn because only a change to
/// another mesh counted.
/// </summary>
public sealed class MeshSwapTests
{
    [Fact]
    public void A_mesh_going_to_none_is_a_change_the_node_follows()
    {
        Assert.True(MeshSwap.Changed(null, "SGameplayObjects.Objects.F_vitreSolo01"));
        Assert.True(MeshSwap.Changed("SQU13_STM.Objects.sqt_murkc_apres", "SQU13_STM.Objects.sqt_murkc_avant"));
        Assert.True(MeshSwap.Changed("SGameplayObjects.Objects.F_vitreSolo01", null));
        Assert.False(MeshSwap.Changed("sgameplayobjects.objects.f_vitresolo01", "SGameplayObjects.Objects.F_vitreSolo01"));
        Assert.False(MeshSwap.Changed(null, null));
    }
}
