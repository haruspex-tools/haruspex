-- Nested CTEs: busy reads loans, and readers reads busy; readers is read again from a scalar subquery and from inside a
-- derived table.
CREATE PROCEDURE lib.nested_ctes
AS
SET NOCOUNT ON;

WITH loans AS (
    SELECT l.member_id, l.book_id
    FROM lib.loan AS l
),
busy AS (
    SELECT x.member_id, COUNT_BIG(*) AS taken
    FROM loans AS x
    GROUP BY x.member_id
),
readers AS (
    SELECT m.member_id, m.name, b.taken
    FROM lib.member AS m
    LEFT JOIN busy AS b ON b.member_id = m.member_id
)
SELECT r.member_id,
       r.taken,
       (SELECT MAX(q.taken) FROM readers AS q) AS most,
       d.name
FROM readers AS r
JOIN (SELECT z.member_id, z.name FROM readers AS z) AS d ON d.member_id = r.member_id
ORDER BY r.member_id;
