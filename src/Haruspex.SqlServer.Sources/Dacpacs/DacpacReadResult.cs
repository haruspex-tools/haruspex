using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>What <see cref="DacpacReader"/> found: the database's model, the packages it was read from, and the problems.</summary>
/// <param name="Model">The schema of the package and the packages of the same database it refers to. When there are errors it is incomplete.</param>
/// <param name="Package">The package read; null when it couldn't be read at all.</param>
/// <param name="References">Every package it refers to, directly or through another, each once, in the order met.</param>
/// <param name="Diagnostics">Problems found while reading, in the order found.</param>
public sealed record DacpacReadResult(DatabaseModel Model, LoadedPackage? Package, EquatableArray<ReferenceRead> References, EquatableArray<Diagnostic> Diagnostics);
