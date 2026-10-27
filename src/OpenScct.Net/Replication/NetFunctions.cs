using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Net;

/// <summary>
/// The arguments of one replicated function call: <see cref="RepParam.ValueOffset"/> gives each parameter's first
/// slot in <see cref="Values"/>; a struct parameter takes one slot per member. Reuse one instance per caller.
/// </summary>
public sealed class NetCallArgs
{
    public NetCallArgs(int capacity = 48)
    {
        Values = new NetValue[capacity];
    }

    public NetValue[] Values { get; }

    /// <summary>Clears every slot (all zero, the value UE2 sends as a single 0 bit).</summary>
    public NetCallArgs Clear()
    {
        Array.Clear(Values);
        return this;
    }

    public NetCallArgs Set(int slot, in NetValue value)
    {
        Values[slot] = value;
        return this;
    }

    public NetCallArgs Bool(int slot, bool value) => Set(slot, NetValue.FromBool(value));

    public NetCallArgs Int(int slot, int value) => Set(slot, NetValue.FromInt(value));

    public NetCallArgs Float(int slot, float value) => Set(slot, NetValue.FromFloat(value));

    public NetCallArgs Vector(int slot, FVector value) => Set(slot, NetValue.FromVector(value));

    public NetCallArgs Rotator(int slot, FRotator value) => Set(slot, NetValue.FromRotator(value));

    public NetCallArgs Object(int slot, UObject? value) => Set(slot, NetValue.FromObject(value));

    public NetCallArgs Name(int slot, Name value) => Set(slot, NetValue.FromName(value));

    public NetCallArgs String(int slot, string? value) => Set(slot, NetValue.FromString(value));
}

/// <summary>Runs a received replicated function on its actor.</summary>
public delegate void NetFunctionHandler(Actor self, RepFunction function, NetCallArgs args, NetConnection from);

/// <summary>What <see cref="NetDriver.ProcessRemoteFunction"/> decided for a call (spec/native/net-replication.md rules 19, 20).</summary>
public enum ENetCallResult : byte
{
    /// <summary>The call runs on this machine and is not sent.</summary>
    RunLocally,

    /// <summary>The call is swallowed: neither sent nor run (a non-simulated function on a proxy).</summary>
    Absorbed,

    /// <summary>The call was sent (or dropped by a saturated connection) and does not run here.</summary>
    Sent,

    /// <summary>A broadcast call: sent to the clients, and it also runs here (a listen server).</summary>
    SentAndRunLocally,
}

/// <summary>
/// Handlers for received calls. A handler registered for a class path and function name runs for that class and its
/// subclasses (the nearest registration wins); without one, the call goes to the C# method of the same name and
/// parameter count on the actor (UnrealScript names are kept in the port), bound once per type with compiled
/// argument conversion.
/// </summary>
public sealed class NetFunctionHandlers
{
    private readonly Dictionary<(string, string), NetFunctionHandler> _handlers = new();
    private readonly Dictionary<(Type, string), NetFunctionHandler?> _bound = new();
    private readonly object _gate = new();

    /// <summary>Calls received for <paramref name="function"/> on <paramref name="classPath"/> (and subclasses) run <paramref name="handler"/>.</summary>
    public void Register(string classPath, string function, NetFunctionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _handlers[(classPath.ToUpperInvariant(), function.ToUpperInvariant())] = handler;
            _bound.Clear();
        }
    }

    /// <summary>
    /// The handler for calls nothing else can run (no registration and no C# port), for example the host's
    /// delivery of a presentation call to the view layer; null drops them (counted as unhandled).
    /// </summary>
    public NetFunctionHandler? Fallback { get; set; }

    /// <summary>The handler for <paramref name="function"/> on <paramref name="actor"/>, or null when nothing can run it.</summary>
    public NetFunctionHandler? Find(Actor actor, RepFunction function)
    {
        Type type = actor.GetType();
        lock (_gate)
        {
            if (_bound.TryGetValue((type, function.Name), out NetFunctionHandler? cached))
            {
                return cached ?? Fallback;
            }

            NetFunctionHandler? handler = null;
            for (UcClassInfo? c = actor.GetUcClass(); c is not null && handler is null; c = c.Super)
            {
                _handlers.TryGetValue((c.Path.ToUpperInvariant(), function.Name.ToUpperInvariant()), out handler);
            }

            handler ??= NetFunctionBinder.Bind(type, function);
            _bound[(type, function.Name)] = handler;
            return handler ?? Fallback;
        }
    }
}

/// <summary>Binds a replicated function to the C# method that ports it.</summary>
public static class NetFunctionBinder
{
    private const BindingFlags MethodFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The C# method of <paramref name="type"/> named like <paramref name="function"/> with its parameter count.</summary>
    public static MethodInfo? FindMethod(Type type, RepFunction function)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(function);
        int count = function.Parameters.Length;
        foreach (StringComparison comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
        {
            for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(MethodFlags))
                {
                    if (string.Equals(m.Name, function.Name, comparison) && m.GetParameters().Length == count && !m.IsGenericMethodDefinition)
                    {
                        return m;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>A handler calling the port of <paramref name="function"/>, or null when <paramref name="type"/> has none.</summary>
    public static NetFunctionHandler? Bind(Type type, RepFunction function)
    {
        MethodInfo? method = FindMethod(type, function);
        if (method is null)
        {
            return null;
        }

        ParameterExpression self = Expression.Parameter(typeof(Actor), "self");
        ParameterExpression args = Expression.Parameter(typeof(NetCallArgs), "args");
        Expression values = Expression.Property(args, nameof(NetCallArgs.Values));
        ParameterInfo[] ps = method.GetParameters();
        var call = new Expression[ps.Length];
        var locals = new List<ParameterExpression>();
        var body = new List<Expression>();
        try
        {
            for (int i = 0; i < ps.Length; i++)
            {
                RepParam p = function.Parameters[i];
                Type target = ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType()! : ps[i].ParameterType;
                if (p.Kind is RepLeafKind kind)
                {
                    call[i] = Convert(Slot(values, p.ValueOffset), kind, target);
                    continue;
                }

                // A struct sent member by member: rebuild it in a local.
                ParameterExpression local = Expression.Variable(target, p.Name);
                locals.Add(local);
                body.Add(Expression.Assign(local, Expression.Default(target)));
                for (int l = 0; l < p.Leaves.Length; l++)
                {
                    RepParamLeaf leaf = p.Leaves[l];
                    Expression member = local;
                    foreach (FieldInfo f in leaf.Fields)
                    {
                        member = Expression.Field(member, f);
                    }

                    body.Add(Expression.Assign(member, Convert(Slot(values, p.ValueOffset + l), leaf.Kind, member.Type)));
                }

                call[i] = local;
            }

            body.Add(Expression.Call(Expression.Convert(self, method.DeclaringType!), method, call));
            Expression block = Expression.Block(locals, body);
            Action<Actor, NetCallArgs> invoke = Expression.Lambda<Action<Actor, NetCallArgs>>(block, self, args).Compile();
            return (actor, _, callArgs, _) => invoke(actor, callArgs);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Expression Slot(Expression values, int index) => Expression.ArrayIndex(values, Expression.Constant(index));

    private static Expression Convert(Expression value, RepLeafKind kind, Type target)
    {
        Expression raw = kind switch
        {
            RepLeafKind.Bool => Expression.NotEqual(Expression.Field(value, nameof(NetValue.I)), Expression.Constant(0)),
            RepLeafKind.Byte or RepLeafKind.Enum or RepLeafKind.Int => Expression.Field(value, nameof(NetValue.I)),
            RepLeafKind.Float => Expression.Field(value, nameof(NetValue.F)),
            RepLeafKind.Name => Expression.Property(value, nameof(NetValue.AsName)),
            RepLeafKind.String => Expression.Property(value, nameof(NetValue.AsString)),
            RepLeafKind.Asset => Expression.New(typeof(AssetRef).GetConstructor([typeof(string)])!,
                Expression.TypeAs(Expression.Field(value, nameof(NetValue.O)), typeof(string))),
            RepLeafKind.Class => Expression.New(typeof(ClassRef).GetConstructor([typeof(string)])!,
                Expression.TypeAs(Expression.Field(value, nameof(NetValue.O)), typeof(string))),
            RepLeafKind.Object => Expression.Field(value, nameof(NetValue.O)),
            RepLeafKind.Vector or RepLeafKind.VectorFine => Expression.Field(value, nameof(NetValue.V)),
            RepLeafKind.Rotator or RepLeafKind.RotatorFine => Expression.Field(value, nameof(NetValue.R)),
            RepLeafKind.Plane => Expression.Call(typeof(NetFunctionBinder).GetMethod(nameof(ToPlane), BindingFlags.Static | BindingFlags.NonPublic)!, value),
            RepLeafKind.CompressedPosition => Expression.Call(
                typeof(NetFunctionBinder).GetMethod(nameof(ToCompressedPosition), BindingFlags.Static | BindingFlags.NonPublic)!, value),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        if (raw.Type == target)
        {
            return raw;
        }

        if (target.IsEnum && raw.Type == typeof(int))
        {
            return Expression.Convert(Expression.Convert(raw, Enum.GetUnderlyingType(target)), target);
        }

        if (!target.IsValueType)
        {
            return Expression.TypeAs(raw, target);
        }

        if (Nullable.GetUnderlyingType(target) is not null)
        {
            return Expression.Convert(raw, target);
        }

        return Expression.Convert(raw, target);
    }

    private static FPlane ToPlane(NetValue v) => new() { X = v.V.X, Y = v.V.Y, Z = v.V.Z, W = v.F };

    private static FCompressedPosition ToCompressedPosition(NetValue v) =>
        new() { Location = v.V, Rotation = v.R, Velocity = v.V2 };
}
