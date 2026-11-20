using OpenScct.Game.Equipment;

namespace OpenScct.Game.SBase;

public partial class SPlayerController
{
    public WeaponCareer WeaponProgress { get; set; } = new();
}
