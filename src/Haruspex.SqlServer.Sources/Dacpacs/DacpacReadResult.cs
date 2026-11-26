using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>What <see cref="DacpacReader"/> found: the database's model, the packages it was read from, and the problems.</summary>
/// <param name="Model">The schema of the package and the packages of the same database it refers to. When there are errors it is incomplete.</param>
/// <param name="Package">The package read; null when it couldn't be read at all.</param>
/// <param name="References">Every package it refers to, directly or through another, each once, in the order met.</param>
/// <param name="Diagnostics">Problems found while reading, in the order found.</param>
public sealed record DacpacReadResult(DatabaseModel Model, LoadedPackage? Package, EquatableArray<ReferenceRead> References, EquatableArray<Diagnostic> Diagnostics)
{
    /// <summary>
    /// The packages read that the config file declared builds (<c>builds</c>), each once, in the order read; empty when none was.
    /// A package that is a build by its origin is here too when <c>builds</c> names it: the declaration is recorded, needed or not.
    /// </summary>
    public EquatableArray<LoadedPackage> ReadAsBuilds { get; init; } = [];
}
