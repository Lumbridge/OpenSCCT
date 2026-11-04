using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Game.Svm;

public sealed partial class Conveyor
{
    public override UcClassInfo GetUcClass() => UcMeta.Class;

    /// <summary>
    /// Metadata of Svm.Conveyor, written by hand: the class is OpenSCCT's own, so it has no original layout (offsets
    /// and sizes are 0) and no script units. It gives the class table, the map loader and the editor the class's
    /// editable properties by their map names.
    /// </summary>
    public static new class UcMeta
    {
        private const UcPropertyFlags Edit = (UcPropertyFlags)0x00000001u;
        private const UcPropertyFlags EditNet = (UcPropertyFlags)0x00000021u;
        private const UcPropertyFlags EditArray = (UcPropertyFlags)0x00400001u;
        private const UcPropertyFlags Net = (UcPropertyFlags)0x00000020u;
        private const string Category = "Conveyor";

        public static readonly UcClassInfo Class = new(
            "Svm.Conveyor",
            typeof(Conveyor),
            Actor.UcMeta.Class,
            (UcClassFlags)0x00000212u,
            null,
            0,
            static () => new Conveyor(),
            [
                Prop("Path", "array<ConveyorPoint>", UcPropertyKind.Array, EditArray),
                Prop("bLoop", "bool", UcPropertyKind.Bool, Edit),
                Prop("Width", "float", UcPropertyKind.Float, Edit),
                Prop("Speed", "float", UcPropertyKind.Float, EditNet),
                Prop("bOn", "bool", UcPropertyKind.Bool, EditNet),
                Prop("bCarryPlayers", "bool", UcPropertyKind.Bool, Edit),
                Prop("bCarryObjects", "bool", UcPropertyKind.Bool, Edit),
                Prop("Look", "EConveyorLook", UcPropertyKind.Enum, Edit),
                Prop("Walls", "EConveyorWalls", UcPropertyKind.Enum, Edit),
                Prop("TriggerAction", "EConveyorTrigger", UcPropertyKind.Enum, Edit),
                Prop("ItemMeshes", "array<StaticMesh>", UcPropertyKind.Array, EditArray),
                Prop("ItemSpacing", "float", UcPropertyKind.Float, Edit),
                Prop("ItemScale", "float", UcPropertyKind.Float, Edit),
                Prop("ItemFade", "float", UcPropertyKind.Float, Edit),
                Prop("ItemSeed", "int", UcPropertyKind.Int, Edit),
                Prop("ItemYawJitter", "int", UcPropertyKind.Int, Edit),
                Prop("ScrollTargets", "array<ConveyorScroll>", UcPropertyKind.Array, EditArray),
                Prop("BeltMaterial", "Material", UcPropertyKind.Asset, Edit),
                Prop("FrameMaterial", "Material", UcPropertyKind.Asset, Edit),
                Prop("Next", "Conveyor", UcPropertyKind.Object, Edit),
                Prop("PhaseAtChange", "float", UcPropertyKind.Float, Net),
                Prop("ChangeTick", "int", UcPropertyKind.Int, Net),
                Prop("SpeedBeforeChange", "float", UcPropertyKind.Float, Net),
            ],
            [],
            [],
            null);

        private static UcPropertyInfo Prop(string name, string type, UcPropertyKind kind, UcPropertyFlags flags) =>
            new(name, type, kind, flags, 1, Category, 0, 0, -1, handWritten: true);
    }
}
