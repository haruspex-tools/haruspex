-- Archived entries found by CONTAINSTABLE, named with their schema from a procedure of another schema that has a table of the
-- same name: KEY is archive.entry's char(8) in its own UTF-8 collation, not lex.entry's int.
CREATE PROCEDURE lex.search_archive @query nvarchar(400) AS
SELECT ft.[KEY] AS code, ft.[RANK] AS rank
FROM CONTAINSTABLE(archive.entry, headword, @query) AS ft;
