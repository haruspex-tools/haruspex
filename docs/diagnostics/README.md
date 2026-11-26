# Diagnostics

Every problem haruspex reports has a stable code, `HAR` and four digits, and a page here saying what it means, why
haruspex reports it and what to do. A code's number, severity and meaning never change; one no longer reported keeps its
page and its number ([decision 0036](../decisions/0036-diagnostics-have-stable-codes.md)). The first digit is the step
the code was born in.

An **error** fails the run (exit code 1 or 2), and its page says what is still written: most stop the run before anything is
written, some leave the output standing, and a run over several modules still writes each module that doesn't fail. A
**warning** doesn't fail the run, and says what the output rests on that
couldn't be checked, or what in the config file can go; a **note** says what the output is, such as an unknown with its
reason.

## Reading the SQL

| Code | Severity | What |
|---|---|---|
| [HAR1001](HAR1001.md) | error | The SQL doesn't parse |
| [HAR1002](HAR1002.md) | error | An object is defined more than once |
| [HAR1003](HAR1003.md) | warning | A referenced DACPAC isn't found |
| [HAR1004](HAR1004.md) | note | Elements of a DACPAC that aren't modelled |
| [HAR1005](HAR1005.md) | error | A file that can't be read as a DACPAC |
| [HAR1007](HAR1007.md) | warning | A referenced project isn't found |
| [HAR1008](HAR1008.md) | warning | Part of a project file isn't read |
| [HAR1009](HAR1009.md) | error | A script a project builds isn't found |
| [HAR1010](HAR1010.md) | error | A file that can't be read as a database project |
| [HAR1011](HAR1011.md) | note | A project's reference isn't read yet (retired) |
| [HAR1012](HAR1012.md) | note | A reference to a database on another server isn't read |
| [HAR1013](HAR1013.md) | note | A table whose definition lists no columns isn't read |
| [HAR1014](HAR1014.md) | error | A query file isn't one batch |
| [HAR1015](HAR1015.md) | error | A query file's parameter declaration can't be taken |
| [HAR1016](HAR1016.md) | error | A query file uses a variable before declaring it |
| [HAR1017](HAR1017.md) | error | A query file returns a value |
| [HAR1018](HAR1018.md) | error | A query file's sets comment can't be taken |
| [HAR1019](HAR1019.md) | error | A query file's module comment can't be taken |
| [HAR1020](HAR1020.md) | error | A script or folder can't be read |
| [HAR1021](HAR1021.md) | warning | A deployment script can't be read in full |
| [HAR1022](HAR1022.md) | error | Names that differ only in case |
| [HAR1023](HAR1023.md) | error | Two of the source's folders hold the same scripts |
| [HAR1024](HAR1024.md) | note | A description that isn't read |

## Inference

| Code | Severity | What |
|---|---|---|
| [HAR2001](HAR2001.md) | note | A statement that isn't modelled yet |
| [HAR2002](HAR2002.md) | note | Results from code outside the project (retired) |
| [HAR2003](HAR2003.md) | note | An object whose definition depends on the order of scripts |
| [HAR2004](HAR2004.md) | note | A query form that isn't modelled yet |
| [HAR2005](HAR2005.md) | note | An expression that isn't modelled yet |
| [HAR2006](HAR2006.md) | note | A table source that isn't modelled yet |
| [HAR2007](HAR2007.md) | note | A SQL type that isn't modelled yet |
| [HAR2008](HAR2008.md) | note | A table the project doesn't define |
| [HAR2009](HAR2009.md) | note | A column that can't be found or is ambiguous |
| [HAR2010](HAR2010.md) | note | A result column without a name |
| [HAR2011](HAR2011.md) | note | Two result columns with the same key |
| [HAR2012](HAR2012.md) | note | Recursion that reached no fixed point |
| [HAR2013](HAR2013.md) | note | JSON whose shape isn't visible |
| [HAR2014](HAR2014.md) | note | A temporary table the caller may create |
| [HAR2015](HAR2015.md) | note | Result sets that depend on the path taken |
| [HAR2016](HAR2016.md) | note | Dynamic SQL (retired) |
| [HAR2017](HAR2017.md) | note | A procedure chosen when the call runs (retired) |
| [HAR2018](HAR2018.md) | note | A system procedure (retired) |
| [HAR2019](HAR2019.md) | note | A procedure that isn't in the project (retired) |
| [HAR2020](HAR2020.md) | note | A procedure name more than one schema has (retired) |
| [HAR2021](HAR2021.md) | note | A trigger that may return result sets |
| [HAR2022](HAR2022.md) | note | A result set with a hidden column |
| [HAR2023](HAR2023.md) | note | A SQLCMD variable a deployment replaces |
| [HAR2024](HAR2024.md) | note | A query file's parameter its uses don't decide |
| [HAR2025](HAR2025.md) | note | JSON built as text |
| [HAR2026](HAR2026.md) | note | A result set returned as FOR XML |
| [HAR2101](HAR2101.md) | warning | A write to an object the project doesn't define |
| [HAR2102](HAR2102.md) | warning | A JSON path that never finds a value |
| [HAR2103](HAR2103.md) | warning | The callers' UNMASK isn't applied |

## Reading the config file

| Code | Severity | What |
|---|---|---|
| [HAR3001](HAR3001.md) | error | A config file can't be read |
| [HAR3002](HAR3002.md) | error | The config file isn't JSON |
| [HAR3003](HAR3003.md) | error | A key is written twice in one object |
| [HAR3004](HAR3004.md) | error | An unknown setting |
| [HAR3005](HAR3005.md) | error | A value of the wrong kind |
| [HAR3006](HAR3006.md) | error | A pattern isn't well formed |
| [HAR3007](HAR3007.md) | error | A procedure's key uses a wildcard |
| [HAR3008](HAR3008.md) | error | A schema has two blocks |
| [HAR3009](HAR3009.md) | error | An exclude without an include |
| [HAR3010](HAR3010.md) | error | A regular expression haruspex can't use |
| [HAR3011](HAR3011.md) | error | A claim without why |
| [HAR3012](HAR3012.md) | error | A setting needs another beside it |
| [HAR3013](HAR3013.md) | error | A why with no claim to explain |
| [HAR3014](HAR3014.md) | error | Paths beside declared results |
| [HAR3015](HAR3015.md) | error | A result is neither a rowset nor JSON, or both |
| [HAR3016](HAR3016.md) | error | A rowset declared as maybe absent |
| [HAR3017](HAR3017.md) | error | A path isn't well formed |
| [HAR3018](HAR3018.md) | error | A value declared nullable |
| [HAR3019](HAR3019.md) | error | A name isn't a valid identifier |
| [HAR3020](HAR3020.md) | error | An override says nothing |
| [HAR3021](HAR3021.md) | error | A references folder isn't there |
| [HAR3022](HAR3022.md) | error | A queries entry outside the config file's folder |
| [HAR3023](HAR3023.md) | error | Set names that can't be used |
| [HAR3024](HAR3024.md) | error | A module that can't be named so |
| [HAR3025](HAR3025.md) | error | A schema in two modules |
| [HAR3101](HAR3101.md) | error | A rowset isn't declared by one row's schema |
| [HAR3102](HAR3102.md) | error | A rowset's required leaves a column out |
| [HAR3103](HAR3103.md) | error | A rowset's column isn't a scalar |
| [HAR3104](HAR3104.md) | error | A JSON document declared nullable |
| [HAR3105](HAR3105.md) | error | A schema without a type |
| [HAR3106](HAR3106.md) | error | A schema of several types (retired) |
| [HAR3107](HAR3107.md) | error | Not a JSON Schema type |
| [HAR3108](HAR3108.md) | error | An object schema without properties (retired) |
| [HAR3109](HAR3109.md) | error | A required key that isn't a property |
| [HAR3110](HAR3110.md) | error | A key both optional and nullable |
| [HAR3111](HAR3111.md) | error | An array schema without items (retired) |
| [HAR3112](HAR3112.md) | error | A reference outside the schema |
| [HAR3113](HAR3113.md) | error | A reference to nothing |
| [HAR3114](HAR3114.md) | error | A recursive schema |
| [HAR3115](HAR3115.md) | error | A column's JSON declared null at its top |
| [HAR3150](HAR3150.md) | warning | A format haruspex doesn't check |
| [HAR3151](HAR3151.md) | warning | A keyword haruspex doesn't check |
| [HAR3152](HAR3152.md) | warning | A keyword ignored beside a reference |

## Applying the config file

| Code | Severity | What |
|---|---|---|
| [HAR4001](HAR4001.md) | error | A schema the project doesn't have |
| [HAR4002](HAR4002.md) | error | An include matches nothing |
| [HAR4003](HAR4003.md) | error | An exclude matches nothing |
| [HAR4004](HAR4004.md) | error | A schema selects nothing |
| [HAR4005](HAR4005.md) | error | An --include matches nothing |
| [HAR4006](HAR4006.md) | error | An --include selects only excluded procedures |
| [HAR4007](HAR4007.md) | error | A queries entry names no query file |
| [HAR4008](HAR4008.md) | error | An object in no module |
| [HAR4009](HAR4009.md) | warning | A module comment where the output isn't split |
| [HAR4101](HAR4101.md) | error | An override of a procedure the project doesn't have |
| [HAR4102](HAR4102.md) | error | An override of a procedure that isn't selected |
| [HAR4103](HAR4103.md) | error | Two overrides of one procedure |
| [HAR4104](HAR4104.md) | error | A path that doesn't exist |
| [HAR4105](HAR4105.md) | error | A path through a value of several types |
| [HAR4106](HAR4106.md) | error | An override of a shared recursive type |
| [HAR4107](HAR4107.md) | error | Nullability declared for JSON text's content |
| [HAR4108](HAR4108.md) | error | Exact values declared without a value set |
| [HAR4109](HAR4109.md) | error | A name for a value that isn't an object |
| [HAR4110](HAR4110.md) | warning | A nullability the inference already proves |
| [HAR4111](HAR4111.md) | warning | Exact values the inference already proves |
| [HAR4112](HAR4112.md) | error | Set names that don't match the result sets |
| [HAR4113](HAR4113.md) | error | A column that can't hold declared JSON |
| [HAR4201](HAR4201.md) | error | Declared results disagree with the inference |
| [HAR4202](HAR4202.md) | warning | Declared results the inference already gives |
| [HAR4203](HAR4203.md) | warning | Declared results nothing uses |
| [HAR4204](HAR4204.md) | warning | Result sets only a declaration can give |
| [HAR4205](HAR4205.md) | warning | Values unknown after a call that isn't followed |
| [HAR4301](HAR4301.md) | error | A procedure that can't be named |
| [HAR4302](HAR4302.md) | error | A type that can't be named |
| [HAR4303](HAR4303.md) | error | Generated names clash |
| [HAR4304](HAR4304.md) | warning | A name the naming rules already give |

## Emitting

| Code | Severity | What |
|---|---|---|
| [HAR5001](HAR5001.md) | error | An IR name isn't a TypeScript identifier |
| [HAR5002](HAR5002.md) | error | An imported name isn't a TypeScript identifier |
| [HAR5003](HAR5003.md) | error | A name is imported from two modules |
| [HAR5004](HAR5004.md) | error | An imported name the module also declares |
| [HAR5005](HAR5005.md) | error | A type read both as rows and as JSON |
| [HAR5006](HAR5006.md) | error | A procedure's parameters' name is taken |
| [HAR5007](HAR5007.md) | error | A result set's name can't be a key |
| [HAR5008](HAR5008.md) | error | A key Zod can't check |
| [HAR5009](HAR5009.md) | error | A column the driver can't hand over |
| [HAR5101](HAR5101.md) | error | One name for two definitions |
| [HAR5201](HAR5201.md) | error | A procedure's function can't take its name |
| [HAR5202](HAR5202.md) | error | A query file's parameter can't be declared |
| [HAR5203](HAR5203.md) | error | A result set's name can't be a key of the function's object |
| [HAR5204](HAR5204.md) | error | A table type's column can't be declared to the driver |
| [HAR5205](HAR5205.md) | error | A parameter the driver can't send |
| [HAR5206](HAR5206.md) | warning | An xml OUTPUT parameter the driver can't receive |

## The command line and files

| Code | Severity | What |
|---|---|---|
| [HAR6001](HAR6001.md) | error | The command line doesn't parse |
| [HAR6002](HAR6002.md) | error | The config file isn't there |
| [HAR6003](HAR6003.md) | error | No source is given |
| [HAR6004](HAR6004.md) | error | The source isn't there |
| [HAR6005](HAR6005.md) | error | No output folder is given |
| [HAR6006](HAR6006.md) | error | An --include pattern isn't well formed |
| [HAR6007](HAR6007.md) | error | Nothing is selected |
| [HAR6008](HAR6008.md) | error | The output can't be written |
| [HAR6009](HAR6009.md) | warning | References with a folder of scripts |
| [HAR6010](HAR6010.md) | warning | A collation the package records otherwise |
| [HAR6011](HAR6011.md) | error | Nothing in the output folder to check against |
| [HAR6012](HAR6012.md) | error | The IR in the output folder is of another version |
| [HAR6013](HAR6013.md) | error | A file in the output folder can't be read |
| [HAR6014](HAR6014.md) | error | What the module claims would change |
| [HAR6015](HAR6015.md) | error | The module in the output folder isn't what haruspex writes |
| [HAR6016](HAR6016.md) | error | A selected procedure's result sets are unknown |
| [HAR6017](HAR6017.md) | error | Unknowns the config file fails |
| [HAR6018](HAR6018.md) | error | No config file below the folder |
| [HAR6019](HAR6019.md) | error | Options that can't go together |
| [HAR6020](HAR6020.md) | error | Two modules write to one folder |
| [HAR6021](HAR6021.md) | error | A config file is already there |
| [HAR6022](HAR6022.md) | error | No database is found to read |
| [HAR6023](HAR6023.md) | error | More than one database to read |
| [HAR6024](HAR6024.md) | error | A code that can't be acknowledged |
| [HAR6025](HAR6025.md) | warning | An acknowledgement that matches no warning |
| [HAR6026](HAR6026.md) | error | A change the running application would fail on |
| [HAR6027](HAR6027.md) | warning | A change the running application ignores |
| [HAR6028](HAR6028.md) | warning | A function's return shape changes |
| [HAR6029](HAR6029.md) | error | The baseline isn't there |
| [HAR6030](HAR6030.md) | error | The baseline can't be read |
| [HAR6031](HAR6031.md) | warning | A query file's sent text isn't checked |
| [HAR6032](HAR6032.md) | error | --fit-only without a baseline |
| [HAR6033](HAR6033.md) | error | A change no one function bridges |
| [HAR6034](HAR6034.md) | warning | A key that may have been renamed |
| [HAR6035](HAR6035.md) | warning | A procedure in only one of the two databases |
| [HAR6036](HAR6036.md) | error | A combined IR given as a database |
| [HAR6037](HAR6037.md) | error | No IR to explain |
| [HAR6038](HAR6038.md) | error | Nothing of that name to explain |
| [HAR6039](HAR6039.md) | warning | The list of files written can't be read |
| [HAR6040](HAR6040.md) | warning | A listed path generate doesn't remove |
| [HAR6041](HAR6041.md) | warning | A file no longer written can't be removed |
| [HAR6042](HAR6042.md) | error | A file generate would remove |
| [HAR6043](HAR6043.md) | warning | A baseline whose claims are another build's |
| [HAR6044](HAR6044.md) | error | Several sources name a project or a package |
