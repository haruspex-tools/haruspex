-- A set operation's columns are named by its first query; the second's names are not used.
CREATE PROCEDURE lib.set_named_by_its_first_query
AS
SET NOCOUNT ON;

SELECT m.name AS who, 'member' AS kind
FROM lib.member AS m
UNION ALL
SELECT b.title AS heading, 'book' AS sort
FROM lib.book AS b
ORDER BY kind, who;
