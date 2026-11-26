-- Notes found by FREETEXTTABLE over every indexed column, without an alias: the call is known by its table's name, note. KEY
-- is the note's code, an nvarchar(20) of its own collation.
CREATE PROCEDURE lex.search_notes @words nvarchar(400) AS
SELECT note.[KEY] AS code, [RANK] AS rank
FROM FREETEXTTABLE(lex.note, *, @words);
