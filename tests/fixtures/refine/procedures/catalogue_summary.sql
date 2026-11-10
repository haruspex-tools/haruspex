-- Aggregates without GROUP BY: exactly one row, even over no titles, as a rowset and as JSON.
CREATE PROCEDURE lend.catalogue_summary
AS
SET NOCOUNT ON;

SELECT COUNT_BIG(*) AS titles, COUNT(t.author) AS with_author, MAX(t.name) AS last_name
FROM lend.title AS t;

SELECT COUNT_BIG(*) AS members, MIN(m.member_id) AS first_id
FROM lend.member AS m
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
