using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SGamePlayObject
{
    /// <summary>
    /// The chaff lock of an object with lights: lights off and the "off" look (TriggeredSkin, else TriggeredMesh,
    /// else the most damaged stage); on exit, an intact object gets its lights and look back.
    /// </summary>
    [Uc("SBase.SGamePlayObject.s_LockedByChaff")]
    public new class s_LockedByChaff : SBasicGamePlayObject.s_LockedByChaff
    {
        public static new readonly s_LockedByChaff Instance = new(UcMeta.s_LockedByChaff, SBasicGamePlayObject.s_LockedByChaff.Instance);

        protected s_LockedByChaff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SGamePlayObject.s_LockedByChaff.BeginState", Sha1 = "b7b7928fc1f51064267f92d593114ab4121183fd")]
        [PortOf("uc:SBase.SGamePlayObject:s_LockedByChaff.BeginState", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
        public override void BeginState(Actor self)
        {
            var gpo = (SGamePlayObject)self;
            gpo.Malfunction();
            if (gpo.ObjectLights.Count == 0)
            {
                return;
            }

            foreach (STriggerLight? light in gpo.ObjectLights)
            {
                (light as IObjectLight)?.LightOff(true);
            }

            if (!gpo.TriggeredSkin.IsNone)
            {
                gpo.SetSkin(gpo.TriggeredSkin);
            }
            else if (!gpo.TriggeredMesh.IsNone)
            {
                gpo.SetStaticMesh(gpo.TriggeredMesh);
            }
            else
            {
                int biggestPercent = -1;
                int biggestIndex = -1;
                for (int i = 0; i < gpo.DamagedMeshes.Count; i++)
                {
                    if (gpo.DamagedMeshes[i].Percent > biggestPercent)
                    {
                        biggestPercent = gpo.DamagedMeshes[i].Percent;
                        biggestIndex = i;
                    }
                }

                if (biggestIndex > -1)
                {
                    gpo.SetStaticMesh(gpo.DamagedMeshes[biggestIndex].StaticMesh);
                }
            }
        }

        [Uc("SBase.SGamePlayObject.s_LockedByChaff.EndState", Sha1 = "d5e4b005307cc1f4d7c9bad6e4921162b16bd0b0")]
        [PortOf("uc:SBase.SGamePlayObject:s_LockedByChaff.EndState", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
        public override void EndState(Actor self)
        {
            var gpo = (SGamePlayObject)self;
            gpo.ChaffLockPreviousState = Name.None;
            if ((gpo.bDamageable && gpo.HitPoints <= 0) || gpo.ObjectLights.Count == 0)
            {
                return;
            }

            foreach (STriggerLight? light in gpo.ObjectLights)
            {
                (light as IObjectLight)?.LightOn();
            }

            if (!gpo.TriggeredSkin.IsNone)
            {
                gpo.SetSkin(gpo.InitialSkin);
            }
            else if (!gpo.TriggeredMesh.IsNone)
            {
                gpo.SetStaticMesh(gpo.InitialStaticMesh);
            }
        }
    }
}
