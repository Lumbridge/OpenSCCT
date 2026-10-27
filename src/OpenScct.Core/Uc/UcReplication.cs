namespace OpenScct.Core.Uc;

/// <summary>Whether a replication block member is a variable or a function.</summary>
public enum UcRepMemberKind : byte
{
    Variable,
    Function,
}

/// <summary>
/// One member of a replication block. For functions, <see cref="FunctionFlags"/> holds the function's flags
/// (FUNC_Net, FUNC_NetReliable, FUNC_Simulated, the SCCT FUNC_NetAllClients...).
/// </summary>
public readonly record struct UcRepMember(string Name, UcRepMemberKind Kind, UcFunctionFlags FunctionFlags = UcFunctionFlags.None)
{
    public bool IsFunction => Kind == UcRepMemberKind.Function;

    /// <summary>The SCCT "to all clients" function flag (0x01000000).</summary>
    public bool IsNetAllClients => (FunctionFlags & UcFunctionFlags.FUNC_NetAllClients) != 0;
}

/// <summary>
/// One condition of a replication block (<c>reliable if (...)</c> or <c>unreliable if (...)</c>) and the members it
/// covers, in source order. <see cref="Offset"/> is the condition's offset in the replication script, which the
/// properties' and functions' RepOffset values point at.
/// </summary>
public sealed class UcRepBlock
{
    public UcRepBlock(bool reliable, int offset, RepExpr condition, UcRepMember[] members)
    {
        Reliable = reliable;
        Offset = offset;
        Condition = condition;
        Members = members ?? [];
    }

    /// <summary>True for <c>reliable if</c>. UE2 uses it for functions; variables always replicate the same way.</summary>
    public bool Reliable { get; }

    public int Offset { get; }

    public RepExpr Condition { get; }

    public UcRepMember[] Members { get; }
}

/// <summary>
/// The replication statement of one class: its blocks in source order, plus the functions this class redefines
/// that keep the replication of the parent class's declaration.
/// </summary>
public sealed class UcReplication
{
    public static readonly UcReplication Empty = new(null, 0, [], []);

    public UcReplication(string? sha1, int scriptSize, UcRepBlock[] blocks, string[] inheritedFunctions)
    {
        Sha1 = sha1;
        ScriptSize = scriptSize;
        Blocks = blocks ?? [];
        InheritedFunctions = inheritedFunctions ?? [];
    }

    /// <summary>SHA-1 (hex) of the replication script bytecode, or null when the class has none.</summary>
    public string? Sha1 { get; }

    public int ScriptSize { get; }

    public UcRepBlock[] Blocks { get; }

    /// <summary>Redefined functions replicated by an ancestor's block.</summary>
    public string[] InheritedFunctions { get; }

    public bool IsEmpty => Blocks.Length == 0 && InheritedFunctions.Length == 0;

    /// <summary>The block that covers <paramref name="member"/> (ASCII case ignored), or null.</summary>
    public UcRepBlock? FindBlock(string member)
    {
        foreach (UcRepBlock block in Blocks)
        {
            foreach (UcRepMember m in block.Members)
            {
                if (string.Equals(m.Name, member, System.StringComparison.OrdinalIgnoreCase))
                {
                    return block;
                }
            }
        }

        return null;
    }
}
