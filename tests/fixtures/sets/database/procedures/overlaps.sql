-- What INTERSECT, UNION and CROSS JOIN must not be claimed to do, each made visible in a run:
-- an INTERSECT of two nullable columns keeps a NULL that is on both sides; an INTERSECT under the database's
-- case-insensitive collation gives the left side's spelling, not the right side's literals; a UNION of two queries of at
-- most one row each gives two rows; a cross join of one-row sides is at most one row under a WHERE, which may remove it.
CREATE PROCEDURE rink.overlaps
    @min bigint
AS
SET NOCOUNT ON;

SELECT m.email
FROM rink.member AS m
INTERSECT
SELECT x.email
FROM rink.member AS x
WHERE x.member_id IN (2, 3)
ORDER BY email;

SELECT s.kind
FROM rink.session AS s
INTERSECT
(SELECT 'PUBLIC' UNION ALL SELECT 'HOCKEY')
ORDER BY kind;

SELECT m.member_id
FROM rink.member AS m
WHERE m.member_id = 1
UNION
SELECT x.member_id
FROM rink.member AS x
WHERE x.member_id = 2
ORDER BY member_id;

SELECT a.members, b.guests
FROM (SELECT COUNT_BIG(*) AS members FROM rink.member) AS a
CROSS JOIN (SELECT COUNT_BIG(*) AS guests FROM rink.guest) AS b
WHERE a.members > @min;
