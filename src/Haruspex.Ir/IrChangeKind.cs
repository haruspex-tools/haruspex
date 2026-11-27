namespace Haruspex.Ir;

/// <summary>
/// How a claim differs between two documents (<see cref="IrComparison"/>). Each kind is one way what a reader of the IR or
/// its generated code receives can change; <see cref="IrChange.Before"/> and <see cref="IrChange.After"/> say from what and
/// to what.
/// </summary>
public enum IrChangeKind
{
    /// <summary>An object that only the later document has.</summary>
    ObjectAdded,

    /// <summary>An object that only the earlier document has.</summary>
    ObjectRemoved,

    /// <summary>The name settled for generated code changed.</summary>
    ObjectRenamed,

    /// <summary>The kind of object changed.</summary>
    ObjectKindChanged,

    /// <summary>The text a query runs changed, which its generated function sends.</summary>
    TextChanged,

    /// <summary>Whether a call may end with a return code other than 0 changed, which changes what its function returns.</summary>
    ReturnsCodeChanged,

    /// <summary>A parameter only the later document has.</summary>
    ParameterAdded,

    /// <summary>A parameter only the earlier document has.</summary>
    ParameterRemoved,

    /// <summary>The parameters both have are in another order.</summary>
    ParametersReordered,

    /// <summary>Whether a caller may leave the parameter out changed.</summary>
    ParameterDefaultChanged,

    /// <summary>Whether the parameter is an output parameter changed.</summary>
    ParameterDirectionChanged,

    /// <summary>The result sets could be listed and now can't.</summary>
    ResultsBecameUnknown,

    /// <summary>The result sets couldn't be listed and now can.</summary>
    ResultsBecameKnown,

    /// <summary>A result set only the later document has.</summary>
    ResultAdded,

    /// <summary>A result set only the earlier document has.</summary>
    ResultRemoved,

    /// <summary>A result set's name changed, was given, or was taken away: the key its function returns it under.</summary>
    ResultRenamed,

    /// <summary>A result is delivered as rows where it was JSON, or the reverse.</summary>
    EncodingChanged,

    /// <summary>How many rows a result's query returns changed.</summary>
    CardinalityChanged,

    /// <summary>A result that always arrived may now be absent.</summary>
    BecameMayBeAbsent,

    /// <summary>A result that could be absent now always arrives.</summary>
    BecameAlwaysPresent,

    /// <summary>A field only the later type has.</summary>
    FieldAdded,

    /// <summary>A field only the earlier type has.</summary>
    FieldRemoved,

    /// <summary>The fields both types have are in another order.</summary>
    FieldsReordered,

    /// <summary>A value proved never null may now be null: a field, an array's items, a record's values, a tuple's element.</summary>
    BecameNullable,

    /// <summary>A value that could be null is now proved never null.</summary>
    BecameNonNull,

    /// <summary>Whether a key is always there or left out when null changed.</summary>
    PresenceChanged,

    /// <summary>The value is of another type: another kind of node, or another scalar kind or format.</summary>
    TypeChanged,

    /// <summary>A value whose type was known is now unknown.</summary>
    BecameUnknown,

    /// <summary>A value whose type was unknown is now known.</summary>
    BecameKnown,

    /// <summary>A named object type is reached under another name.</summary>
    TypeRenamed,

    /// <summary>The source's own type of a scalar changed, as written: informational, but part of what the module says.</summary>
    SqlTypeChanged,

    /// <summary>
    /// A type the IR names but doesn't hold the definition of, such as an <c>xml</c> schema collection or a CLR type, so it can't
    /// be shown the same in both documents, though named alike. Only <see cref="IrFit"/> finds it.
    /// </summary>
    TypeUnproven,

    /// <summary>
    /// A table type's definition changed under its name (0.26): a column, a key, a condition, how a column is filled or its
    /// collation, as the definitions the two documents carry say.
    /// </summary>
    TableTypeChanged,

    /// <summary>A string's maximum or fixed length changed.</summary>
    LengthChanged,

    /// <summary>A string's minimum length changed.</summary>
    MinLengthChanged,

    /// <summary>A decimal's precision and scale changed.</summary>
    DigitsChanged,

    /// <summary>A number's lower bound changed.</summary>
    MinChanged,

    /// <summary>A number's upper bound changed.</summary>
    MaxChanged,

    /// <summary>The fewest or most items an array has changed.</summary>
    ItemsBoundsChanged,

    /// <summary>A value set has values it didn't have.</summary>
    ValuesAdded,

    /// <summary>A value set lacks values it had.</summary>
    ValuesRemoved,

    /// <summary>A value not drawn from a set now is.</summary>
    ValueSetAdded,

    /// <summary>A value drawn from a set no longer is.</summary>
    ValueSetRemoved,

    /// <summary>How a value relates to its set's items changed, or the fold that gives a value's canonical form.</summary>
    ValueMatchChanged,

    /// <summary>The source's dialect changed.</summary>
    DialectChanged,

    /// <summary>The options the callers' sessions are declared to set changed.</summary>
    SessionChanged,

    /// <summary>
    /// A package the source read is now declared a build in the config file, or no longer is (0.31): its primary keys and unique
    /// constraints are claimed, or no longer, on the user's word.
    /// </summary>
    BuildsChanged,

    /// <summary>
    /// A folder is now read as part of the source, or no longer is (0.32): the database is made of other scripts. Compared only where
    /// either document's source was read from several folders.
    /// </summary>
    FoldersChanged,

    /// <summary>A fold a canonical value set names was added, removed or compares otherwise.</summary>
    FoldChanged,

    /// <summary>
    /// A column's declared JSON (0.28) was added, removed, or reads another schema: another file or pointer, or a file whose text
    /// changed, which may change what every value read from the column is claimed to hold.
    /// </summary>
    ColumnDeclarationChanged,

    /// <summary>
    /// An endpoint's declared response (0.36) was added, removed, or reads another schema: another file or pointer, or a file whose
    /// text changed, which may change what every reply read from the endpoint is claimed to hold.
    /// </summary>
    EndpointDeclarationChanged,

    /// <summary>
    /// A parameter's declared JSON (0.37) was added, removed, or reads another schema: another file or pointer, or a file whose text
    /// changed, which may change what the body's reads of the parameter are claimed to hold and what a caller may send.
    /// </summary>
    ParameterDeclarationChanged,
}
