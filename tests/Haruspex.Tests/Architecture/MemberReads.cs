using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// Which members of other assemblies each method of an assembly calls, read from its IL: every call, virtual call, constructor
/// call and method pointer whose operand is a member of another assembly. A lambda, a local
/// function and an iterator's or async method's state machine are compiled to methods of their own; each is counted as the
/// method it is written in, so a read inside a lambda is the enclosing method's.
/// </summary>
internal static class MemberReads
{
    /// <summary>Every one-byte and two-byte opcode, by its value, for the size of its operand.</summary>
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    /// <summary>
    /// Each call in an assembly of a member of another, as <c>Type.Method: Namespace.Type.member</c> with the method written by
    /// hand that makes it (<see cref="WrittenIn"/>), in order and once each. Only types whose name satisfies
    /// <paramref name="types"/> are read.
    /// </summary>
    /// <param name="assembly">The assembly's file.</param>
    /// <param name="types">Which of its types to read, by name.</param>
    public static IReadOnlyList<string> Of(string assembly, Func<string, bool> types)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            var writtenIn = WrittenIn(metadata, method);
            if (method.RelativeVirtualAddress == 0 || !types(writtenIn.Split('.')[0]))
                continue;
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
            foreach (var member in Calls(metadata, il))
                found.Add($"{writtenIn}: {member}");
        }

        return [.. found];
    }

    /// <summary>The members of other assemblies a method body's IL calls, as <c>Namespace.Type.member</c>.</summary>
    private static IEnumerable<string> Calls(MetadataReader metadata, BlobReader il)
    {
        while (il.RemainingBytes > 0)
        {
            short value = il.ReadByte();
            if (value == 0xFE)
                value = (short)(0xFE00 | il.ReadByte());
            var code = OpCodesByValue[value];
            switch (code.OperandType)
            {
                case OperandType.InlineMethod:
                    var token = il.ReadInt32();
                    if (Referenced(metadata, MetadataTokens.EntityHandle(token)) is { } member)
                        yield return member;
                    break;
                case OperandType.InlineSwitch:
                    // The count first: in Offset += 4 * ReadInt32(), the offset would be read before the count moves it.
                    var targets = il.ReadInt32();
                    il.Offset += 4 * targets;
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    il.Offset += 1;
                    break;
                case OperandType.InlineVar:
                    il.Offset += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    il.Offset += 8;
                    break;
                default:
                    il.Offset += 4;
                    break;
            }
        }
    }

    /// <summary>A method or constructor of another assembly, as <c>Namespace.Type.member</c>; null for one of this assembly's own.</summary>
    private static string? Referenced(MetadataReader metadata, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodSpecification:
                return Referenced(metadata, metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
            case HandleKind.MemberReference:
                var reference = metadata.GetMemberReference((MemberReferenceHandle)handle);
                return TypeName(metadata, reference.Parent) is { } type ? $"{type}.{metadata.GetString(reference.Name)}" : null;
            default:
                return null;
        }
    }

    /// <summary>The full name of a referenced type, or of a generic one's definition; null for a type of this assembly.</summary>
    private static string? TypeName(MetadataReader metadata, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
                var type = metadata.GetTypeReference((TypeReferenceHandle)handle);
                return $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
            case HandleKind.TypeSpecification:
                // A generic instantiation: its signature starts with its kind, then whether it is a class or a struct, then the definition.
                var blob = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                    return null;
                blob.ReadSignatureTypeCode();
                return TypeName(metadata, blob.ReadTypeHandle());
            default:
                return null;
        }
    }

    /// <summary>
    /// The method, written by hand, that a compiled method belongs to, as <c>Type.Method</c>: itself, or for a lambda, local
    /// function or state machine (named <c>&lt;Method&gt;…</c>, or in a type so named) the method it was written in.
    /// </summary>
    private static string WrittenIn(MetadataReader metadata, MethodDefinition method)
    {
        var name = metadata.GetString(method.Name);
        var type = metadata.GetTypeDefinition(method.GetDeclaringType());
        var typeName = metadata.GetString(type.Name);
        while (typeName.StartsWith('<') && type.GetDeclaringType() is { IsNil: false } outer)
        {
            if (!name.StartsWith('<') && typeName.IndexOf('>', StringComparison.Ordinal) > 1)
                name = typeName;
            type = metadata.GetTypeDefinition(outer);
            typeName = metadata.GetString(type.Name);
        }

        if (name.StartsWith('<') && name.IndexOf('>', StringComparison.Ordinal) is > 1 and var end)
            name = name[1..end];
        return $"{typeName}.{name}";
    }
}
