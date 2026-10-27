using System;
using OpenScct.Provenance;

namespace OpenScct.Core.Uc;

/// <summary>UnrealScript class flags (UClass::ClassFlags), with the UE2 names.</summary>
[Flags]
[Provenance(ProvenanceKind.Decoded, "Class flag bits checked against the reflection dumps of the game and editor")]
public enum UcClassFlags : uint
{
    None = 0,
    CLASS_Abstract = 0x00000001,
    CLASS_Compiled = 0x00000002,
    CLASS_Config = 0x00000004,
    CLASS_Transient = 0x00000008,
    CLASS_Parsed = 0x00000010,
    CLASS_Localized = 0x00000020,
    CLASS_SafeReplace = 0x00000040,
    CLASS_RuntimeStatic = 0x00000080,
    CLASS_NoExport = 0x00000100,
    CLASS_Placeable = 0x00000200,
    CLASS_PerObjectConfig = 0x00000400,
    CLASS_NativeReplication = 0x00000800,
    CLASS_EditInlineNew = 0x00001000,
    CLASS_CollapseCategories = 0x00002000,
    CLASS_IsAUProperty = 0x00008000,
    CLASS_IsAUObjectProperty = 0x00010000,
    CLASS_IsAUBoolProperty = 0x00020000,
    CLASS_IsAUState = 0x00040000,
    CLASS_IsAUFunction = 0x00080000,

    /// <summary>Set on some SCCT classes; meaning not known yet.</summary>
    CLASS_Bit21 = 0x00200000,

    /// <summary>Set on some SCCT classes; meaning not known yet.</summary>
    CLASS_Bit22 = 0x00400000,
}

/// <summary>UnrealScript property flags (UProperty::PropertyFlags, CPF_*), also used for function parameters.</summary>
[Flags]
[Provenance(ProvenanceKind.Decoded, "Property flag bits checked against the reflection dumps of the game and editor")]
public enum UcPropertyFlags : uint
{
    None = 0,
    CPF_Edit = 0x00000001,
    CPF_Const = 0x00000002,
    CPF_Input = 0x00000004,
    CPF_ExportObject = 0x00000008,
    CPF_OptionalParm = 0x00000010,
    CPF_Net = 0x00000020,
    CPF_EditConstArray = 0x00000040,
    CPF_Parm = 0x00000080,
    CPF_OutParm = 0x00000100,
    CPF_SkipParm = 0x00000200,
    CPF_ReturnParm = 0x00000400,
    CPF_CoerceParm = 0x00000800,
    CPF_Native = 0x00001000,
    CPF_Transient = 0x00002000,
    CPF_Config = 0x00004000,
    CPF_Localized = 0x00008000,
    CPF_Travel = 0x00010000,
    CPF_EditConst = 0x00020000,
    CPF_GlobalConfig = 0x00040000,
    CPF_OnDemand = 0x00100000,
    CPF_New = 0x00200000,
    CPF_NeedCtorLink = 0x00400000,
    CPF_NoExport = 0x00800000,
    CPF_EditInline = 0x04000000,
    CPF_EdFindable = 0x08000000,
    CPF_EditInlineUse = 0x10000000,
    CPF_Deprecated = 0x20000000,
    CPF_EditInlineNotify = 0x40000000,

    /// <summary>Set on two SCCT properties; meaning not known yet.</summary>
    CPF_Bit31 = 0x80000000,
}

/// <summary>UnrealScript function flags (UFunction::FunctionFlags, FUNC_*).</summary>
[Flags]
[Provenance(ProvenanceKind.Decoded, "Function flag bits checked against the reflection dumps of the game and editor")]
public enum UcFunctionFlags : uint
{
    None = 0,
    FUNC_Final = 0x00000001,
    FUNC_Defined = 0x00000002,
    FUNC_Iterator = 0x00000004,
    FUNC_Latent = 0x00000008,
    FUNC_PreOperator = 0x00000010,
    FUNC_Singular = 0x00000020,
    FUNC_Net = 0x00000040,
    FUNC_NetReliable = 0x00000080,
    FUNC_Simulated = 0x00000100,
    FUNC_Exec = 0x00000200,
    FUNC_Native = 0x00000400,
    FUNC_Event = 0x00000800,
    FUNC_Operator = 0x00001000,
    FUNC_Static = 0x00002000,
    FUNC_NoExport = 0x00004000,
    FUNC_Const = 0x00008000,
    FUNC_Invariant = 0x00010000,
    FUNC_Public = 0x00020000,
    FUNC_Private = 0x00040000,
    FUNC_Protected = 0x00080000,
    FUNC_Delegate = 0x00100000,

    /// <summary>
    /// SCCT addition: a replicated function sent to every client the actor is relevant to, not only its owner
    /// (spec/objectives/messages.md, spec/objectives/missions.md). Always set together with FUNC_Net.
    /// </summary>
    FUNC_NetAllClients = 0x01000000,
}

/// <summary>UnrealScript state flags (UState::StateFlags, STATE_*).</summary>
[Flags]
public enum UcStateFlags : uint
{
    None = 0,
    STATE_Editable = 0x00000001,
    STATE_Auto = 0x00000002,
    STATE_Simulated = 0x00000004,
}
