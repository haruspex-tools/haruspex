-- A report that filters out the nulls: authors with their titles, and the last genre where any title has one.
CREATE PROCEDURE lend.authors_report
AS
SET NOCOUNT ON;

SELECT t.author, COUNT_BIG(*) AS titles, MAX(t.genre) AS genre, MIN(t.isbn) AS first_isbn
FROM lend.title AS t
WHERE t.author IS NOT NULL
GROUP BY t.author
HAVING MAX(t.genre) IS NOT NULL
ORDER BY t.author;
