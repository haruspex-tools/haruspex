using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// Which members each method of an assembly uses, read from its IL: every call, virtual call, constructor call and method
/// pointer whose operand is a member of another assembly (<see cref="Of"/>), or every such call and every field read,
/// written or addressed whose operand is a member of the same assembly (<see cref="Own"/>). A lambda, a local function and an
/// iterator's or async method's state machine are compiled to methods of their own; each is counted as the method it is
/// written in, so a read inside a lambda is the enclosing method's.
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
            foreach (var token in Operands(il, OperandType.InlineMethod))
            {
                if (Referenced(metadata, MetadataTokens.EntityHandle(token)) is { } member)
                    found.Add($"{writtenIn}: {member}");
            }
        }

        return [.. found];
    }

    /// <summary>
    /// Each use in an assembly of a member declared in that assembly: a method, constructor or property accessor called or
    /// pointed to, and a field read, written or addressed, as <c>Type.Method: Owner.member</c>, where <c>Owner</c> is the
    /// member's type with the types it is nested in (<see cref="NestedName"/>), with the method written by hand that makes it
    /// (<see cref="WrittenIn"/>), in order and once each. Only methods whose type, named so, satisfies <paramref name="types"/>
    /// are read; a lambda's or state machine's type is nested in the type of the method it is written in, so it is read with it.
    /// </summary>
    /// <param name="assembly">The assembly's file.</param>
    /// <param name="types">Which of its types to read, by the name with the types each is nested in.</param>
    public static IReadOnlyList<string> Own(string assembly, Func<string, bool> types)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0 || !types(NestedName(metadata, method.GetDeclaringType())))
                continue;
            var writtenIn = WrittenIn(metadata, method);
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
            foreach (var token in Operands(il, OperandType.InlineMethod, OperandType.InlineField))
            {
                if (Declared(metadata, MetadataTokens.EntityHandle(token)) is { } member)
                    found.Add($"{writtenIn}: {member}");
            }
        }

        return [.. found];
    }

    /// <summary>A type's name with the types it is nested in, outermost first, joined by <c>+</c> as reflection joins them.</summary>
    private static string NestedName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(type.Name);
        return type.GetDeclaringType() is { IsNil: false } outer ? $"{NestedName(metadata, outer)}+{name}" : name;
    }

    /// <summary>
    /// A method or field declared in the assembly being read, as <c>Owner.member</c> (<see cref="NestedName"/>); null for a
    /// member of another assembly.
    /// </summary>
    private static string? Declared(MetadataReader metadata, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return $"{NestedName(metadata, method.GetDeclaringType())}.{metadata.GetString(method.Name)}";
            case HandleKind.FieldDefinition:
                var field = metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
                return $"{NestedName(metadata, field.GetDeclaringType())}.{metadata.GetString(field.Name)}";
            case HandleKind.MethodSpecification:
                return Declared(metadata, metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
            case HandleKind.MemberReference:
                // A member of a generic type of this assembly, used at its type arguments: the parent names the instantiation.
                var reference = metadata.GetMemberReference((MemberReferenceHandle)handle);
                if (reference.Parent.Kind != HandleKind.TypeSpecification)
                    return null;
                var blob = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)reference.Parent).Signature);
                if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                    return null;
                blob.ReadSignatureTypeCode();
                return blob.ReadTypeHandle() is { Kind: HandleKind.TypeDefinition } definition
                    ? $"{NestedName(metadata, (TypeDefinitionHandle)definition)}.{metadata.GetString(reference.Name)}"
                    : null;
            default:
                return null;
        }
    }

    /// <summary>The tokens a method body's IL gives instructions whose operand is of one of <paramref name="kinds"/>, in order.</summary>
    private static IEnumerable<int> Operands(BlobReader il, params OperandType[] kinds)
    {
        while (il.RemainingBytes > 0)
        {
            short value = il.ReadByte();
            if (value == 0xFE)
                value = (short)(0xFE00 | il.ReadByte());
            var code = OpCodesByValue[value];
            switch (code.OperandType)
            {
                case var kind when kinds.Contains(kind):
                    yield return il.ReadInt32();
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
