using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>An object whose keys are data: what <c>json_objectagg</c> builds.</summary>
internal sealed record RecordShape(Shape Values, EquatableArray<IrNullReason> ValuesNullableBecause) : Shape;
