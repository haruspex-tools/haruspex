-- Labels found by CONTAINSTABLE: KEY is the alias type's char(8).
CREATE PROCEDURE lex.search_labels @query nvarchar(400) AS
SELECT ft.[KEY] AS ref, ft.[RANK] AS rank
FROM CONTAINSTABLE(lex.label, words, @query) AS ft;
