using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Model;

/// <summary>
/// A Zod schema expression, as the lowering stage builds it and the printer writes it: a call chain on <c>z</c>, an object
/// schema, a union, a reference to a schema defined elsewhere, JSON text, a note for the reader, or a literal argument. The set
/// is closed (the constructor is private), so the printer and anything else that reads an expression handle every case.
/// </summary>
/// <remarks>
/// The model is Zod's vocabulary rather than TypeScript's syntax tree: it holds what the generated code says, and leaves how
/// it is written (quotes, escapes, commas, line breaks) to the printer, the only stage that knows them. Records compare by
/// value, so two expressions that would print the same are equal. Why a model at all, rather than strings or a full TypeScript
/// AST, is argued in <c>docs/internals/11-emitting-code.md</c>.
/// </remarks>
internal abstract record ZodExpression
{
    private ZodExpression()
    {
    }

    /// <summary>A call of one of <c>z</c>'s schema builders: <c>z.string()</c>, <c>z.array(…)</c>.</summary>
    public static ZodExpression Z(string builder, params ZodExpression[] arguments) => new Call(new Name(ZodModule.ZodName), builder, [.. arguments]);

    /// <summary>This expression with a method called on it: <c>.max(10)</c>, <c>.nullable()</c>.</summary>
    public ZodExpression Then(string method, params ZodExpression[] arguments) => new Call(this, method, [.. arguments]);

    /// <summary>
    /// A name in scope, or a property path from one, written as it is: <c>z</c>, <c>z.iso</c>, <c>OrderSchema</c>, or a schema
    /// the config file imports. A reference to a schema defined elsewhere is one.
    /// </summary>
    /// <param name="Path">The name or path.</param>
    public sealed record Name(string Path) : ZodExpression;

    /// <summary>A method called on a target: one link of a call chain such as <c>z.int().min(0)</c>.</summary>
    /// <param name="Target">What the method is called on.</param>
    /// <param name="Method">The method's name.</param>
    /// <param name="Arguments">The arguments, in order.</param>
    public sealed record Call(ZodExpression Target, string Method, EquatableArray<ZodExpression> Arguments) : ZodExpression;

    /// <summary><c>z.union([…])</c>: a value that matches the first of its variants that accepts it.</summary>
    /// <param name="Variants">The variants, in the order they are tried.</param>
    public sealed record Union(EquatableArray<ZodExpression> Variants) : ZodExpression;

    /// <summary><c>z.object({…})</c>, or <c>z.strictObject({…})</c>: a named object type's schema, one property per field.</summary>
    /// <param name="Properties">The fields, in order.</param>
    /// <param name="Strict">Whether a key the type does not list fails parsing, rather than being removed.</param>
    public sealed record ObjectSchema(EquatableArray<ZodProperty> Properties, bool Strict) : ZodExpression;

    /// <summary>A string that must parse as JSON to a value of <paramref name="Content"/>, through the module's helper.</summary>
    /// <param name="Content">The schema of what the text holds.</param>
    public sealed record JsonText(ZodExpression Content) : ZodExpression;

    /// <summary>
    /// An expression with a note for the reader of the generated file, such as why a check the IR states was left out. The
    /// note is not code: the printer writes it beside the property that holds the expression.
    /// </summary>
    /// <param name="Expression">The expression the note is about.</param>
    /// <param name="Note">The note, in plain words.</param>
    public sealed record Noted(ZodExpression Expression, string Note) : ZodExpression;

    /// <summary>A number argument, as decimal text written as it is: <c>0</c>, <c>-32768</c>, <c>99.99</c>, <c>1e-2</c>.</summary>
    /// <param name="Value">The number.</param>
    public sealed record Number(string Value) : ZodExpression;

    /// <summary>A string argument: a value of an enum, say. The printer quotes and escapes it.</summary>
    /// <param name="Value">The string.</param>
    public sealed record Text(string Value) : ZodExpression;

    /// <summary>A boolean argument, as in <c>{ local: true }</c>.</summary>
    /// <param name="Value">The value.</param>
    public sealed record Boolean(bool Value) : ZodExpression;

    /// <summary>
    /// A regular expression argument matching exactly one of <paramref name="Values"/> followed by any number of spaces, as a
    /// value of a padded type compares equal to its value set. The printer writes the pattern and escapes each value.
    /// </summary>
    /// <param name="Values">The values, as they are, without their trailing spaces.</param>
    public sealed record PaddedValues(EquatableArray<string> Values) : ZodExpression;

    /// <summary>A number argument that is a power of ten, such as a decimal's step: <c>1</c>, <c>1e-2</c>.</summary>
    /// <param name="Exponent">The power, zero or less for a step.</param>
    public sealed record PowerOfTen(int Exponent) : ZodExpression;

    /// <summary>An array argument: <c>[a, b]</c>, as <c>z.tuple</c>, <c>z.enum</c> and <c>z.literal</c> take.</summary>
    /// <param name="Items">The items, in order.</param>
    public sealed record ArrayLiteral(EquatableArray<ZodExpression> Items) : ZodExpression;

    /// <summary>An object argument written on one line: <c>{ local: true }</c>, or the shape <c>.extend</c> takes.</summary>
    /// <param name="Properties">The properties, in order.</param>
    public sealed record ObjectLiteral(EquatableArray<ZodProperty> Properties) : ZodExpression;
}
