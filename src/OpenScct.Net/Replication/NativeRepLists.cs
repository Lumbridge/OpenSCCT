using System;
using System.Collections.Generic;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>
/// The decoded native property lists (<c>GetOptimizedRepList</c> of every replicated slice class), as replacement
/// conditions for the script ones (spec/native/net-replists.md). All 59 classes with replicated variables carry
/// NativeReplication, so in the original these lists, not the script blocks, decide what is sent.
/// <para>
/// A rule names a class, a property and the condition the class's list applies to it; a class whose list does not
/// call its parent's (<see cref="ClassRules.NoParent"/>) sends nothing it does not list. For a property of an
/// actor of class X, the rule of the nearest class from X upwards that lists the property wins; a class with
/// NoParent met on the way, without a rule for the property, stops it (never sent); with no rule at all the
/// script condition stands.
/// </para>
/// <para>
/// Not expressible as a condition and not ported here: the pawn list's 0.09 s cadence and dead-reckoning samples
/// (spec/native/net-proxy.md rules 1 to 6), "Location when the actor just lost or gained its Base", the one-shot
/// flags (<c>bForceReplicationForSpecTarget</c>, <c>replicateGadgets</c>,
/// <c>bForceReplicationForCurrentLightType</c> are read but not cleared), PlayerController's "Role when the last
/// sent value was not Authority", the inverted <c>bIgnoreEchelonLighting</c> test of SAmmoStock and
/// SOrientableObject's per-user <c>fVisionFOV</c>.
/// </para>
/// </summary>
[Provenance(ProvenanceKind.Decoded, "spec/native/net-replists.md rules 1 to 28 (editor and game GetOptimizedRepList bodies)")]
public static class NativeRepLists
{
    /// <summary>The rules of one class's list.</summary>
    public sealed class ClassRules
    {
        internal ClassRules(string classPath, bool noParent, IReadOnlyDictionary<string, string> rules)
        {
            ClassPath = classPath;
            NoParent = noParent;
            Rules = rules;
        }

        public string ClassPath { get; }

        /// <summary>The list does not call its parent's: properties it does not list are never sent.</summary>
        public bool NoParent { get; }

        /// <summary>Property name to condition text (ASCII case ignored).</summary>
        public IReadOnlyDictionary<string, string> Rules { get; }
    }

    private const string Skip = "(!bSkipActorPropertyReplication || bNetInitial)";
    private const string Auth = "Role == ROLE_Authority";
    private const string Initial = "bNetInitial && Role == ROLE_Authority";
    private const string Never = "false";

    // Movement group of the Actor list (rule 4): only with bReplicateMovement or in the first bunch, never with
    // bCompressedPosition for Location and Velocity, never Location or Rotation in a map actor's first bunch.
    private const string Movement = Skip + " && (bReplicateMovement || bNetInitial)";
    private const string WorldBase = "(Base == None || Base.bWorldGeometry)";
    private const string RotationDrawTypes = "(DrawType == DT_Mesh || DrawType == DT_StaticMesh || DrawType == DT_Particle)";

    private static readonly Dictionary<string, ClassRules> s_rules = Build();

    /// <summary>Every class with rules, by class path (ASCII case ignored).</summary>
    public static IReadOnlyDictionary<string, ClassRules> All => s_rules;

    /// <summary>The rules of <paramref name="classPath"/>, or null.</summary>
    public static ClassRules? Find(string classPath) => s_rules.GetValueOrDefault(classPath);

    /// <summary>
    /// The native condition text for <paramref name="propertyName"/> on an actor of class <paramref name="cls"/>:
    /// the rule text, <c>"false"</c> when a list without a parent call hides it, or null for the script condition.
    /// </summary>
    public static string? Resolve(UcClassInfo cls, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(cls);
        for (UcClassInfo? c = cls; c is not null; c = c.Super)
        {
            if (!s_rules.TryGetValue(c.Path, out ClassRules? rules))
            {
                continue;
            }

            if (rules.Rules.TryGetValue(propertyName, out string? text))
            {
                return text;
            }

            if (rules.NoParent)
            {
                return Never;
            }
        }

        return null;
    }

    private static Dictionary<string, ClassRules> Build()
    {
        var all = new Dictionary<string, ClassRules>(StringComparer.OrdinalIgnoreCase);

        void Add(string path, bool noParent, params (string Property, string Condition)[] rules)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string property, string condition) in rules)
            {
                map[property] = condition;
            }

            all[path] = new ClassRules(path, noParent, map);
        }

        // ---- Engine.Actor (rules 1 to 8) ----------------------------------------------------------------------
        Add("Engine.Actor", false,
            // 2: first bunch only.
            ("bCollideActors", "bNetInitial"),
            ("bCollideWorld", "bNetInitial"),
            ("bEMFIsOn", Initial),
            ("EMFOpacity", Initial),
            ("EMFIntensity", Initial),
            ("HeatIntensity", Initial),
            ("bUnlit", Initial),
            ("bIgnoreEchelonLighting", Initial),
            ("bIgnoreDynamicEchelonLighting", Initial),
            ("bSpecialLit", Initial),
            // 4: movement.
            ("Location", Movement + " && !bNetInitial && !bCompressedPosition && RemoteRole != ROLE_AutonomousProxy && " +
                WorldBase + " && (RemoteRole == ROLE_DumbProxy || bUpdateSimulatedPosition)"),
            ("Rotation", Movement + " && !bCompressedPosition && " + RotationDrawTypes + " && (RemoteRole == ROLE_AutonomousProxy && bNetInitial || " +
                "RemoteRole != ROLE_AutonomousProxy && " + WorldBase + " && (RemoteRole == ROLE_DumbProxy || bUpdateSimulatedPosition || bNetInitial) && " +
                "!(bNetInitial && (bNetInitialRotation || bStatic || bNoDelete)))"),
            ("Base", Never),
            ("Velocity", Movement + " && !bCompressedPosition && (RemoteRole == ROLE_SimulatedProxy && (bNetInitial || bUpdateSimulatedPosition) || " +
                "RemoteRole == ROLE_DumbProxy && Physics == PHYS_Falling)"),
            ("Physics", Movement + " && (RemoteRole == ROLE_SimulatedProxy && bNetInitial || RemoteRole == ROLE_DumbProxy)"),
            // 5: ambient sound, first bunch only, not to the owner of a bClientAnim actor.
            ("AmbientSound", Skip + " && " + Initial + " && (!bNetOwner || !bClientAnim)"),
            ("SoundOcclusion", Skip + " && " + Initial + " && (!bNetOwner || !bClientAnim) && AmbientSound != None"),
            ("SoundRadius", Skip + " && " + Initial + " && (!bNetOwner || !bClientAnim) && AmbientSound != None"),
            ("SoundVolume", Skip + " && " + Initial + " && (!bNetOwner || !bClientAnim) && AmbientSound != None"),
            ("SoundPitch", Skip + " && " + Initial + " && (!bNetOwner || !bClientAnim) && AmbientSound != None"),
            // 6: dirty group; Owner to the owning connection only; StaticMesh in the first bunch for static meshes.
            ("Owner", Skip + " && " + Auth + " && bNetDirty && bNetOwner"),
            ("StaticMesh", Skip + " && " + Initial + " && DrawType == DT_StaticMesh"),
            ("LightType", Skip + " && " + Auth + " && bNetDirty"),
            ("UsesLightBeam", Skip + " && " + Auth + " && bNetDirty"),
            // 7: first bunch only.
            ("bHidden", Skip + " && " + Initial),
            ("Role", Skip + " && " + Initial),
            ("RemoteRole", Skip + " && " + Initial),
            ("bNetOwner", Skip + " && " + Initial),
            ("bTearOff", Skip + " && " + Initial),
            // 8: never.
            ("AmbientGlow", Never),
            ("SoundBone", Never));

        // ---- infos (rules 10, 11) -----------------------------------------------------------------------------
        Add("Engine.GameReplicationInfo", false,
            ("VoiceChatMode", Never),
            ("ServerRegion", Never),
            ("ShortName", Never));
        Add("Engine.LevelInfo", false,
            ("Pauser", Auth + " && bNetDirty"),
            ("TimeDilation", Auth + " && bNetDirty"),
            ("NbPointsAttack", Auth + " && bNetDirty"),
            ("NbPointsDefense", Auth + " && bNetDirty"),
            ("GEActivationMask", Auth),
            ("DefaultGravity", Never));

        // ---- SPlayerController (rule 9) ------------------------------------------------------------------------
        Add("SBase.SPlayerController", false,
            ("MyDesiredRotation", Initial));

        // ---- pawns (rule 12) ----------------------------------------------------------------------------------
        const string NonOwnerInitial = "bNetInitial && !bNetOwner && " + Auth;
        Add("SBase.SBasePawn", false,
            ("PoisonnedBy", Auth),
            ("bGasMask", Auth),
            ("bPoisonned", Auth),
            ("UsedObject", Auth),
            ("Role", Auth),
            ("RemoteRole", Auth),
            ("bTearOff", Auth),
            ("bEMFIsOn", Auth),
            ("EMFOpacity", Auth),
            ("EMFIntensity", Auth),
            ("HeatIntensity", Auth),
            ("bHidden", Auth),
            ("AmbientSound", Auth),
            ("HandItem", Auth),
            ("CarriedFlag", Auth),
            ("m_AttackedBy", Auth),
            ("m_AttackTarget", Auth),
            ("m_InteractionTarget", Auth),
            ("m_PawnToTerminate", Auth),
            ("bCanBeGrabbed", Auth),
            ("bSpyBulletOn", Auth),
            ("iTeamSpying", Auth),
            ("aGadgetType", Auth),
            ("aNbChargesGad", Auth),
            ("WeaponLoaded", Initial),
            ("bIsCrouched", Initial),
            ("m_PawnNoMoreGrabbed", Initial),
            ("PipHFire", Initial),
            ("BTW", Initial),
            ("IK_Type", Initial),
            ("bBeginSpecial12Anim", Initial),
            ("Physics", Initial),
            ("AllActions", NonOwnerInitial),
            ("iNbActionForDistantClient", NonOwnerInitial),
            ("fRepTimeOfLastActionChange", NonOwnerInitial),
            ("ZoneConfigForClient", NonOwnerInitial),
            ("MyWeapon", Auth + " && bNetOwner"),
            ("MyGadget", Auth + " && bNetOwner && replicateGadgets"),
            ("Base", Auth + " && !bNetOwner"),
            ("PawnPosition", Never),
            ("bIsWalking", Never),
            ("bIsTyping", Never),
            ("GroundSpeed", Never),
            ("AccelRate", Never),
            ("AirSpeed", Never),
            ("JumpZ", Never),
            ("AirControl", Never));

        // ---- map objects (rules 13 to 27) ---------------------------------------------------------------------
        Add("SBase.SBasicGamePlayObject", false,
            ("AmbientGlow", Initial),
            ("StaticMesh", Auth + " && DrawType == DT_StaticMesh"));
        Add("SBase.SGamePlayObject", false,
            ("Base", Auth + " && bPickable"));
        Add("SBase.SAnimatedMesh", false,
            ("bHidden", Auth));
        Add("SBase.SObjectiveTrigger", false,
            ("bEMFIsOn", Auth),
            ("EMFOpacity", Auth),
            ("EMFIntensity", Auth),
            ("HeatIntensity", Auth),
            ("bActive", Auth),
            ("bLocked", Auth),
            ("CaptureTime", Auth + " && bActive"),
            ("TriggerTime", Auth + " && bActive"),
            ("Objective", Auth + " && bActive"),
            ("bTriggering", Auth + " && bActive"));
        Add("SBase.SComputerObjectiveTrigger", false,
            ("bHidden", Auth),
            ("bCollideActors", Auth),
            ("bBlockPlayers", Auth),
            ("UseRelativeRotation", Initial),
            ("UseRelativeLocation", Initial),
            ("DominationCaptureTime", Initial),
            ("DeltaCaptureTime", Auth + " && bActive"),
            ("SkinState", Auth));
        Add("SBase.SFlag", false,
            ("Base", Auth),
            ("bHidden", Auth),
            ("bTriggering", Auth),
            ("FlagCarrier", Auth),
            ("bResetZeroValues", Auth),
            ("DropZone", Initial));
        Add("SBase.SDisk", false,
            ("Base", Auth),
            ("bHidden", Auth),
            ("bTriggering", Auth));
        Add("SBase.SFlagDropZone", true,
            ("bTriggering", Auth));
        Add("SBase.SObjective", true,
            ("ObjectiveTaken", Auth),
            ("MinTriggerTime", Auth),
            ("iSimulatedObjectiveIsBeingTookUntil", Auth));
        Add("SBase.SMission", false,
            ("ObjectivesLeft", Auth),
            ("TotalObjectivesLeft", Auth));
        Add("SBase.SInteractObject", false,
            ("ItRadius", Initial),
            ("ItHeight", Initial));
        Add("SBase.SCamNetwork", false,
            ("iReplicatedVisionMode", Never));
        Add("SBase.STriggerLight", false,
            ("bEMFIsOn", Auth),
            ("EMFOpacity", Auth),
            ("EMFIntensity", Auth),
            ("HeatIntensity", Auth),
            ("LightType", Auth + " && bForceReplicationForCurrentLightType"));
        Add("Engine.Mover", false,
            ("BaseRot", Initial),
            ("SimOldPos", Auth),
            ("SimInterpolate", Auth),
            ("RealPosition", Auth),
            ("AmbientSound", Auth),
            ("Velocity", Auth),
            ("BasePos", Never));
        Add("SBase.SLiftDoor", false,
            ("BasePos", Auth),
            ("Base", Auth));
        Add("SBase.SAmmoStock", true,
            ("bIgnoreEchelonLighting", Initial),
            ("bIgnoreDynamicEchelonLighting", Initial),
            ("bSpecialLit", Initial));
        Add("SBase.SAmbientSoundTrigger", true,
            ("AmbientSound", Auth));
        Add("SBase.SSmokeEmitter", true,
            ("AmbientSound", Auth));

        // ASAlarm::GetOptimizedRepList (game 0x10a8ea90, spec/devices/alarm.md Network): no parent list.
        Add("SBase.SAlarm", true,
            ("Duration", Initial),
            ("MessagePriority", Initial),
            ("bAlarmOn", Auth),
            ("StartTime", Auth));

        // ---- inventory (rule 28) ------------------------------------------------------------------------------
        Add("SBase.SWeapon", false,
            ("bEMFIsOn", Auth),
            ("EMFOpacity", Auth),
            ("EMFIntensity", Auth),
            ("HeatIntensity", Auth));
        Add("SBase.SGoggleBeam", false,
            ("bHidden", Auth));
        return all;
    }
}
