namespace Haruspex.Ir;

/// <summary>
/// Any JSON value (0.28): an object, an array, a string, a number, a boolean or <c>null</c>, of any content. A real type, not an
/// unknown: what holds it is known to be JSON, so it nests as JSON wherever JSON nests, and a consumer can check that it is JSON,
/// though nothing about its shape is claimed. A column declared by the config file with the schema <c>{}</c> or <c>true</c>, which
/// allows every JSON value, holds JSON of this type. Why: docs/decisions/0094-any-json-is-a-type-of-its-own.md.
/// </summary>
/// <param name="ObjectOrArray">
/// Whether it is only ever an object or an array, as SQL Server's <c>JSON_QUERY</c> returns: what it reads is any JSON, but a
/// scalar there gives null (or an error), never the scalar.
/// </param>
public sealed record IrAnyJson(bool ObjectOrArray = false) : IrType;
