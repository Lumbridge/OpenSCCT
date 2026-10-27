using System;

namespace OpenScct.Core.Uc;

/// <summary>One parameter of an UnrealScript function: name, declared type and flags (out, optional, coerce).</summary>
public readonly record struct UcParamInfo(string Name, string Type, UcPropertyFlags Flags)
{
    public bool IsOut => (Flags & UcPropertyFlags.CPF_OutParm) != 0;

    public bool IsOptional => (Flags & UcPropertyFlags.CPF_OptionalParm) != 0;

    public bool IsCoerce => (Flags & UcPropertyFlags.CPF_CoerceParm) != 0;

    public bool IsSkip => (Flags & UcPropertyFlags.CPF_SkipParm) != 0;
}

/// <summary>
/// One UnrealScript function (a coverage unit): name, bytecode SHA-1, flags and signature. No body: ports are
/// written by hand from the specs and carry <c>[Uc(UcPath, Sha1 = Sha1)]</c>.
/// </summary>
public sealed class UcFunctionInfo
{
    public UcFunctionInfo(string name, string sha1, UcFunctionFlags flags, int nativeIndex, int scriptSize,
        string? returnType, UcParamInfo[] parameters, int repOffset = -1, int operPrecedence = 0, string[]? latentCalls = null)
    {
        Name = name;
        Sha1 = sha1;
        Flags = flags;
        NativeIndex = nativeIndex;
        ScriptSize = scriptSize;
        ReturnType = returnType;
        Parameters = parameters ?? [];
        RepOffset = repOffset;
        OperPrecedence = operPrecedence;
        LatentCalls = latentCalls ?? [];
    }

    public string Name { get; }

    /// <summary>SHA-1 (hex) of the original bytecode, the value <c>[Uc]</c> attributes carry.</summary>
    public string Sha1 { get; }

    public UcFunctionFlags Flags { get; }

    /// <summary>Native function index (iNative), 0 for none.</summary>
    public int NativeIndex { get; }

    /// <summary>In-memory bytecode size, the port weight.</summary>
    public int ScriptSize { get; }

    /// <summary>Return type as declared, or null for none.</summary>
    public string? ReturnType { get; }

    public UcParamInfo[] Parameters { get; }

    /// <summary>Offset of the replication condition in the replication script, or -1.</summary>
    public int RepOffset { get; }

    public int OperPrecedence { get; }

    /// <summary>Latent functions the body calls (FinishAnim, Sleep...), sorted.</summary>
    public string[] LatentCalls { get; }

    /// <summary>The declaring class.</summary>
    public UcClassInfo Class { get; internal set; } = null!;

    /// <summary>The state that declares the function, or null for a class-level function.</summary>
    public UcStateInfo? State { get; internal set; }

    public bool IsNative => (Flags & UcFunctionFlags.FUNC_Native) != 0;

    public bool IsSimulated => (Flags & UcFunctionFlags.FUNC_Simulated) != 0;

    public bool IsNet => (Flags & UcFunctionFlags.FUNC_Net) != 0;

    public bool IsReliable => (Flags & UcFunctionFlags.FUNC_NetReliable) != 0;

    /// <summary>The SCCT "to all clients" replicated function flag (0x01000000).</summary>
    public bool IsNetAllClients => (Flags & UcFunctionFlags.FUNC_NetAllClients) != 0;

    public bool IsLatent => (Flags & UcFunctionFlags.FUNC_Latent) != 0;

    public bool IsExec => (Flags & UcFunctionFlags.FUNC_Exec) != 0;

    public bool IsEvent => (Flags & UcFunctionFlags.FUNC_Event) != 0;

    public bool IsStatic => (Flags & UcFunctionFlags.FUNC_Static) != 0;

    public bool IsFinal => (Flags & UcFunctionFlags.FUNC_Final) != 0;

    public bool IsDelegate => (Flags & UcFunctionFlags.FUNC_Delegate) != 0;

    /// <summary>
    /// Path for <c>[Uc]</c>: <c>Pkg.Class.Func</c> or <c>Pkg.Class.State.Func</c>, for example
    /// <c>SBase.SInterrupteur.Trigger</c>.
    /// </summary>
    public string UcPath => State is null ? Class.Path + "." + Name : Class.Path + "." + State.Name + "." + Name;

    /// <summary>
    /// Coverage unit id: <c>nat:Pkg.Class.Func</c> for natives, else <c>uc:Pkg.Class:Func</c> or
    /// <c>uc:Pkg.Class:State.Func</c> (the inventory's forms).
    /// </summary>
    public string UnitId => IsNative
        ? "nat:" + Class.Path + "." + Name
        : State is null ? "uc:" + Class.Path + ":" + Name : "uc:" + Class.Path + ":" + State.Name + "." + Name;

    public bool HasLatentCall(string name) => Array.IndexOf(LatentCalls, name) >= 0;

    public override string ToString() => Class is null ? Name : UcPath;
}
