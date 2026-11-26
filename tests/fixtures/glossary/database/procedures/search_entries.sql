-- Entries ranked by a CONTAINSTABLE search of their words: KEY is an int, the entry's id.
CREATE PROCEDURE lex.search_entries @query nvarchar(400) AS
SELECT e.id, e.headword, found.[KEY] AS found_id, found.[RANK] AS rank
FROM lex.entry AS e
JOIN CONTAINSTABLE(lex.entry, (headword, definition), @query, LANGUAGE 1033, 50) AS found ON found.[KEY] = e.id
ORDER BY found.[RANK] DESC;
