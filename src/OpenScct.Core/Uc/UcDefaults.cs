using System;
using System.Collections.Generic;

namespace OpenScct.Core.Uc;

/// <summary>Small helpers the generated field initializers and SetDefaults methods use.</summary>
public static class UcDefaults
{
    /// <summary>A string static array with every element empty (UnrealScript strings are never null).</summary>
    public static string[] Strings(int length)
    {
        var array = new string[length];
        Array.Fill(array, string.Empty);
        return array;
    }

    /// <summary>Replaces the contents of a dynamic array with <paramref name="items"/>.</summary>
    public static void Set<T>(List<T> list, params ReadOnlySpan<T> items)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.Clear();
        list.AddRange(items);
    }
}
