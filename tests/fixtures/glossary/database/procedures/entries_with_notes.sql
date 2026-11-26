-- Every entry, with the notes a search finds for it when there are any: a left join, so KEY and RANK may be null.
CREATE PROCEDURE lex.entries_with_notes @words nvarchar(400) AS
SELECT e.id, n.code, found.[KEY] AS note_code, found.[RANK] AS rank
FROM lex.entry AS e
LEFT JOIN lex.note AS n ON n.entry_id = e.id
LEFT JOIN FREETEXTTABLE(lex.note, body, @words) AS found ON found.[KEY] = n.code;
