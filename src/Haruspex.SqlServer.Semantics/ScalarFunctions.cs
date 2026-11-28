namespace Haruspex.SqlServer.Semantics;

/// <summary>The built-in scalar functions, one value per row from their arguments, in the catalogue (<see cref="BuiltInFunctions"/>).</summary>
public static class ScalarFunctions
{
    /// <summary><c>DATEDIFF</c>.</summary>
    public static readonly BuiltInFunction DateDiff = new("datediff", FunctionForm.Scalar);

    /// <summary><c>DATEDIFF_BIG</c>.</summary>
    public static readonly BuiltInFunction DateDiffBig = new("datediff_big", FunctionForm.Scalar);

    /// <summary><c>DATEADD</c>.</summary>
    public static readonly BuiltInFunction DateAdd = new("dateadd", FunctionForm.Scalar);

    /// <summary><c>DATEPART</c>.</summary>
    public static readonly BuiltInFunction DatePart = new("datepart", FunctionForm.Scalar);

    /// <summary><c>YEAR</c>.</summary>
    public static readonly BuiltInFunction Year = new("year", FunctionForm.Scalar);

    /// <summary><c>MONTH</c>.</summary>
    public static readonly BuiltInFunction Month = new("month", FunctionForm.Scalar);

    /// <summary><c>DAY</c>.</summary>
    public static readonly BuiltInFunction Day = new("day", FunctionForm.Scalar);

    /// <summary><c>DATENAME</c>.</summary>
    public static readonly BuiltInFunction DateName = new("datename", FunctionForm.Scalar);

    /// <summary><c>EOMONTH</c>.</summary>
    public static readonly BuiltInFunction EndOfMonth = new("eomonth", FunctionForm.Scalar);

    /// <summary><c>DATEFROMPARTS</c>.</summary>
    public static readonly BuiltInFunction DateFromParts = new("datefromparts", FunctionForm.Scalar);

    /// <summary><c>DATETIMEFROMPARTS</c>.</summary>
    public static readonly BuiltInFunction DateTimeFromParts = new("datetimefromparts", FunctionForm.Scalar);

    /// <summary><c>SMALLDATETIMEFROMPARTS</c>.</summary>
    public static readonly BuiltInFunction SmallDateTimeFromParts = new("smalldatetimefromparts", FunctionForm.Scalar);

    /// <summary><c>DATETIME2FROMPARTS</c>.</summary>
    public static readonly BuiltInFunction DateTime2FromParts = new("datetime2fromparts", FunctionForm.Scalar);

    /// <summary><c>TIMEFROMPARTS</c>.</summary>
    public static readonly BuiltInFunction TimeFromParts = new("timefromparts", FunctionForm.Scalar);

    /// <summary><c>DATETIMEOFFSETFROMPARTS</c>.</summary>
    public static readonly BuiltInFunction DateTimeOffsetFromParts = new("datetimeoffsetfromparts", FunctionForm.Scalar);

    /// <summary><c>DATETRUNC</c>.</summary>
    public static readonly BuiltInFunction DateTruncate = new("datetrunc", FunctionForm.Scalar);

    /// <summary><c>DATE_BUCKET</c>.</summary>
    public static readonly BuiltInFunction DateBucket = new("date_bucket", FunctionForm.Scalar);

    /// <summary><c>SWITCHOFFSET</c>.</summary>
    public static readonly BuiltInFunction SwitchOffset = new("switchoffset", FunctionForm.Scalar);

    /// <summary><c>TODATETIMEOFFSET</c>.</summary>
    public static readonly BuiltInFunction ToDateTimeOffset = new("todatetimeoffset", FunctionForm.Scalar);

    /// <summary>
    /// <c>AT TIME ZONE</c>, which T-SQL writes as an operator after its date (<c>d AT TIME ZONE 'UTC'</c>) and ScriptDom parses as a
    /// call of its own; its name is as T-SQL writes it, so no call by name can mean it.
    /// </summary>
    public static readonly BuiltInFunction AtTimeZone = new("at time zone", FunctionForm.Scalar);

    /// <summary><c>ISDATE</c>.</summary>
    public static readonly BuiltInFunction IsDate = new("isdate", FunctionForm.Scalar);

    /// <summary><c>GETDATE</c>.</summary>
    public static readonly BuiltInFunction GetDate = new("getdate", FunctionForm.Scalar);

    /// <summary><c>GETUTCDATE</c>.</summary>
    public static readonly BuiltInFunction GetUtcDate = new("getutcdate", FunctionForm.Scalar);

    /// <summary><c>SYSDATETIME</c>.</summary>
    public static readonly BuiltInFunction SysDateTime = new("sysdatetime", FunctionForm.Scalar);

    /// <summary><c>SYSUTCDATETIME</c>.</summary>
    public static readonly BuiltInFunction SysUtcDateTime = new("sysutcdatetime", FunctionForm.Scalar);

    /// <summary><c>SYSDATETIMEOFFSET</c>.</summary>
    public static readonly BuiltInFunction SysDateTimeOffset = new("sysdatetimeoffset", FunctionForm.Scalar);

    /// <summary><c>CURRENT_TIMESTAMP</c>.</summary>
    public static readonly BuiltInFunction CurrentTimestamp = new("current_timestamp", FunctionForm.Scalar);

    /// <summary><c>LTRIM</c>.</summary>
    public static readonly BuiltInFunction LTrim = new("ltrim", FunctionForm.Scalar);

    /// <summary><c>RTRIM</c>.</summary>
    public static readonly BuiltInFunction RTrim = new("rtrim", FunctionForm.Scalar);

    /// <summary><c>TRIM</c>.</summary>
    public static readonly BuiltInFunction Trim = new("trim", FunctionForm.Scalar);

    /// <summary><c>UPPER</c>.</summary>
    public static readonly BuiltInFunction Upper = new("upper", FunctionForm.Scalar);

    /// <summary><c>LOWER</c>.</summary>
    public static readonly BuiltInFunction Lower = new("lower", FunctionForm.Scalar);

    /// <summary><c>SUBSTRING</c>.</summary>
    public static readonly BuiltInFunction Substring = new("substring", FunctionForm.Scalar);

    /// <summary><c>LEFT</c>.</summary>
    public static readonly BuiltInFunction Left = new("left", FunctionForm.Scalar);

    /// <summary><c>RIGHT</c>.</summary>
    public static readonly BuiltInFunction Right = new("right", FunctionForm.Scalar);

    /// <summary><c>LEN</c>.</summary>
    public static readonly BuiltInFunction Len = new("len", FunctionForm.Scalar);

    /// <summary><c>DATALENGTH</c>.</summary>
    public static readonly BuiltInFunction DataLength = new("datalength", FunctionForm.Scalar);

    /// <summary><c>REPLACE</c>.</summary>
    public static readonly BuiltInFunction Replace = new("replace", FunctionForm.Scalar);

    /// <summary><c>STRING_ESCAPE</c>.</summary>
    public static readonly BuiltInFunction StringEscape = new("string_escape", FunctionForm.Scalar);

    /// <summary><c>FORMATMESSAGE</c>.</summary>
    public static readonly BuiltInFunction FormatMessage = new("formatmessage", FunctionForm.Scalar);

    /// <summary><c>CONCAT</c>.</summary>
    public static readonly BuiltInFunction Concat = new("concat", FunctionForm.Scalar);

    /// <summary><c>CONCAT_WS</c>.</summary>
    public static readonly BuiltInFunction ConcatWithSeparator = new("concat_ws", FunctionForm.Scalar);

    /// <summary><c>ASCII</c>.</summary>
    public static readonly BuiltInFunction Ascii = new("ascii", FunctionForm.Scalar);

    /// <summary><c>UNICODE</c>.</summary>
    public static readonly BuiltInFunction Unicode = new("unicode", FunctionForm.Scalar);

    /// <summary><c>CHAR</c>: named for what it gives, a character of a code, since <c>Char</c> is a type's name (CA1720).</summary>
    public static readonly BuiltInFunction CharOf = new("char", FunctionForm.Scalar);

    /// <summary><c>CHARINDEX</c>.</summary>
    public static readonly BuiltInFunction CharIndex = new("charindex", FunctionForm.Scalar);

    /// <summary><c>PATINDEX</c>.</summary>
    public static readonly BuiltInFunction PatIndex = new("patindex", FunctionForm.Scalar);

    /// <summary><c>REPLICATE</c>.</summary>
    public static readonly BuiltInFunction Replicate = new("replicate", FunctionForm.Scalar);

    /// <summary><c>SPACE</c>.</summary>
    public static readonly BuiltInFunction Space = new("space", FunctionForm.Scalar);

    /// <summary><c>REVERSE</c>.</summary>
    public static readonly BuiltInFunction Reverse = new("reverse", FunctionForm.Scalar);

    /// <summary><c>STUFF</c>.</summary>
    public static readonly BuiltInFunction Stuff = new("stuff", FunctionForm.Scalar);

    /// <summary><c>QUOTENAME</c>.</summary>
    public static readonly BuiltInFunction QuoteName = new("quotename", FunctionForm.Scalar);

    /// <summary><c>STR</c>.</summary>
    public static readonly BuiltInFunction Str = new("str", FunctionForm.Scalar);

    /// <summary><c>TRANSLATE</c>.</summary>
    public static readonly BuiltInFunction Translate = new("translate", FunctionForm.Scalar);

    /// <summary><c>SOUNDEX</c>.</summary>
    public static readonly BuiltInFunction Soundex = new("soundex", FunctionForm.Scalar);

    /// <summary><c>DIFFERENCE</c>.</summary>
    public static readonly BuiltInFunction Difference = new("difference", FunctionForm.Scalar);

    /// <summary><c>ROUND</c>.</summary>
    public static readonly BuiltInFunction Round = new("round", FunctionForm.Scalar);

    /// <summary><c>FLOOR</c>.</summary>
    public static readonly BuiltInFunction Floor = new("floor", FunctionForm.Scalar);

    /// <summary><c>CEILING</c>.</summary>
    public static readonly BuiltInFunction Ceiling = new("ceiling", FunctionForm.Scalar);

    /// <summary><c>ABS</c>.</summary>
    public static readonly BuiltInFunction Abs = new("abs", FunctionForm.Scalar);

    /// <summary><c>SIGN</c>.</summary>
    public static readonly BuiltInFunction Sign = new("sign", FunctionForm.Scalar);

    /// <summary><c>POWER</c>.</summary>
    public static readonly BuiltInFunction Power = new("power", FunctionForm.Scalar);

    /// <summary><c>SQUARE</c>.</summary>
    public static readonly BuiltInFunction Square = new("square", FunctionForm.Scalar);

    /// <summary><c>SQRT</c>.</summary>
    public static readonly BuiltInFunction Sqrt = new("sqrt", FunctionForm.Scalar);

    /// <summary><c>EXP</c>.</summary>
    public static readonly BuiltInFunction Exp = new("exp", FunctionForm.Scalar);

    /// <summary><c>LOG</c>.</summary>
    public static readonly BuiltInFunction Log = new("log", FunctionForm.Scalar);

    /// <summary><c>LOG10</c>.</summary>
    public static readonly BuiltInFunction Log10 = new("log10", FunctionForm.Scalar);

    /// <summary><c>SIN</c>.</summary>
    public static readonly BuiltInFunction Sin = new("sin", FunctionForm.Scalar);

    /// <summary><c>COS</c>.</summary>
    public static readonly BuiltInFunction Cos = new("cos", FunctionForm.Scalar);

    /// <summary><c>TAN</c>.</summary>
    public static readonly BuiltInFunction Tan = new("tan", FunctionForm.Scalar);

    /// <summary><c>COT</c>.</summary>
    public static readonly BuiltInFunction Cot = new("cot", FunctionForm.Scalar);

    /// <summary><c>ASIN</c>.</summary>
    public static readonly BuiltInFunction Asin = new("asin", FunctionForm.Scalar);

    /// <summary><c>ACOS</c>.</summary>
    public static readonly BuiltInFunction Acos = new("acos", FunctionForm.Scalar);

    /// <summary><c>ATAN</c>.</summary>
    public static readonly BuiltInFunction Atan = new("atan", FunctionForm.Scalar);

    /// <summary><c>ATN2</c>.</summary>
    public static readonly BuiltInFunction Atn2 = new("atn2", FunctionForm.Scalar);

    /// <summary><c>DEGREES</c>.</summary>
    public static readonly BuiltInFunction Degrees = new("degrees", FunctionForm.Scalar);

    /// <summary><c>RADIANS</c>.</summary>
    public static readonly BuiltInFunction Radians = new("radians", FunctionForm.Scalar);

    /// <summary><c>PI</c>.</summary>
    public static readonly BuiltInFunction Pi = new("pi", FunctionForm.Scalar);

    /// <summary><c>CAST</c>.</summary>
    public static readonly BuiltInFunction Cast = new("cast", FunctionForm.Scalar);

    /// <summary><c>CONVERT</c>.</summary>
    public static readonly BuiltInFunction Convert = new("convert", FunctionForm.Scalar);

    /// <summary><c>TRY_CAST</c>.</summary>
    public static readonly BuiltInFunction TryCast = new("try_cast", FunctionForm.Scalar);

    /// <summary><c>TRY_CONVERT</c>.</summary>
    public static readonly BuiltInFunction TryConvert = new("try_convert", FunctionForm.Scalar);

    /// <summary><c>ISNULL</c>.</summary>
    public static readonly BuiltInFunction IsNull = new("isnull", FunctionForm.Scalar);

    /// <summary><c>COALESCE</c>.</summary>
    public static readonly BuiltInFunction Coalesce = new("coalesce", FunctionForm.Scalar);

    /// <summary><c>NULLIF</c>.</summary>
    public static readonly BuiltInFunction NullIf = new("nullif", FunctionForm.Scalar);

    /// <summary><c>IIF</c>.</summary>
    public static readonly BuiltInFunction Iif = new("iif", FunctionForm.Scalar);

    /// <summary><c>CHOOSE</c>.</summary>
    public static readonly BuiltInFunction Choose = new("choose", FunctionForm.Scalar);

    /// <summary><c>GREATEST</c>.</summary>
    public static readonly BuiltInFunction Greatest = new("greatest", FunctionForm.Scalar);

    /// <summary><c>LEAST</c>.</summary>
    public static readonly BuiltInFunction Least = new("least", FunctionForm.Scalar);

    /// <summary><c>JSON_VALUE</c>.</summary>
    public static readonly BuiltInFunction JsonValue = new("json_value", FunctionForm.Scalar);

    /// <summary><c>JSON_QUERY</c>.</summary>
    public static readonly BuiltInFunction JsonQuery = new("json_query", FunctionForm.Scalar);

    /// <summary><c>JSON_OBJECT</c>.</summary>
    public static readonly BuiltInFunction JsonObject = new("json_object", FunctionForm.Scalar);

    /// <summary><c>JSON_ARRAY</c>.</summary>
    public static readonly BuiltInFunction JsonArray = new("json_array", FunctionForm.Scalar);

    /// <summary><c>ISJSON</c>.</summary>
    public static readonly BuiltInFunction IsJson = new("isjson", FunctionForm.Scalar);

    /// <summary><c>NEWID</c>.</summary>
    public static readonly BuiltInFunction NewId = new("newid", FunctionForm.Scalar);

    /// <summary><c>HASHBYTES</c>.</summary>
    public static readonly BuiltInFunction HashBytes = new("hashbytes", FunctionForm.Scalar);

    /// <summary><c>COMPRESS</c>.</summary>
    public static readonly BuiltInFunction Compress = new("compress", FunctionForm.Scalar);

    /// <summary><c>DECOMPRESS</c>.</summary>
    public static readonly BuiltInFunction Decompress = new("decompress", FunctionForm.Scalar);

    /// <summary><c>PARSENAME</c>.</summary>
    public static readonly BuiltInFunction ParseName = new("parsename", FunctionForm.Scalar);

    /// <summary><c>DB_NAME</c>.</summary>
    public static readonly BuiltInFunction DbName = new("db_name", FunctionForm.Scalar);

    /// <summary><c>@@ROWCOUNT</c>, a global variable, which ScriptDom parses as a node of its own.</summary>
    public static readonly BuiltInFunction RowCount = new("@@rowcount", FunctionForm.Scalar);

    /// <summary><c>ROWCOUNT_BIG</c>.</summary>
    public static readonly BuiltInFunction RowCountBig = new("rowcount_big", FunctionForm.Scalar);

    /// <summary><c>@@TRANCOUNT</c>.</summary>
    public static readonly BuiltInFunction TranCount = new("@@trancount", FunctionForm.Scalar);

    /// <summary><c>@@ERROR</c>.</summary>
    public static readonly BuiltInFunction Error = new("@@error", FunctionForm.Scalar);

    /// <summary><c>REGEXP_REPLACE</c>.</summary>
    public static readonly BuiltInFunction RegexpReplace = new("regexp_replace", FunctionForm.Scalar);

    /// <summary><c>REGEXP_SUBSTR</c>.</summary>
    public static readonly BuiltInFunction RegexpSubstr = new("regexp_substr", FunctionForm.Scalar);

    /// <summary><c>REGEXP_INSTR</c>.</summary>
    public static readonly BuiltInFunction RegexpInstr = new("regexp_instr", FunctionForm.Scalar);

    /// <summary><c>REGEXP_COUNT</c>.</summary>
    public static readonly BuiltInFunction RegexpCount = new("regexp_count", FunctionForm.Scalar);

    /// <summary>Every one, in the order above.</summary>
    public static IReadOnlyList<BuiltInFunction> All { get; } = [DateDiff, DateDiffBig, DateAdd, DatePart, Year, Month, Day, DateName, EndOfMonth, DateFromParts, DateTimeFromParts, SmallDateTimeFromParts, DateTime2FromParts, TimeFromParts, DateTimeOffsetFromParts, DateTruncate, DateBucket, SwitchOffset, ToDateTimeOffset, AtTimeZone, IsDate, GetDate, GetUtcDate, SysDateTime, SysUtcDateTime, SysDateTimeOffset, CurrentTimestamp, LTrim, RTrim, Trim, Upper, Lower, Substring, Left, Right, Len, DataLength, Replace, StringEscape, FormatMessage, Concat, ConcatWithSeparator, Ascii, Unicode, CharOf, CharIndex, PatIndex, Replicate, Space, Reverse, Stuff, QuoteName, Str, Translate, Soundex, Difference, Round, Floor, Ceiling, Abs, Sign, Power, Square, Sqrt, Exp, Log, Log10, Sin, Cos, Tan, Cot, Asin, Acos, Atan, Atn2, Degrees, Radians, Pi, Cast, Convert, TryCast, TryConvert, IsNull, Coalesce, NullIf, Iif, Choose, Greatest, Least, JsonValue, JsonQuery, JsonObject, JsonArray, IsJson, NewId, HashBytes, Compress, Decompress, ParseName, DbName, RowCount, RowCountBig, TranCount, Error, RegexpReplace, RegexpSubstr, RegexpInstr, RegexpCount];
}
