-- A CROSS JOIN of two counts is one row; a derived table over a UNION whose queries each filter out their NULLs is never
-- null; a CROSS JOIN of sessions and a VALUES list of sizes reads both sides as they are.
CREATE PROCEDURE rink.counts
AS
SET NOCOUNT ON;

SELECT a.members, b.guests
FROM (SELECT COUNT_BIG(*) AS members FROM rink.member) AS a
CROSS JOIN (SELECT COUNT_BIG(*) AS guests FROM rink.guest) AS b;

SELECT p.person_id, p.contact
FROM (SELECT m.member_id AS person_id, m.email AS contact
      FROM rink.member AS m
      WHERE m.email IS NOT NULL
      UNION
      SELECT g.guest_id, g.phone
      FROM rink.guest AS g
      WHERE g.phone IS NOT NULL) AS p
ORDER BY p.person_id, p.contact;

WITH busy AS (
    SELECT b.session_id FROM rink.booking AS b
    EXCEPT
    SELECT s.session_id FROM rink.session AS s WHERE s.kind = 'hockey'
)
SELECT s.session_id, s.coach_id, z.size
FROM busy
JOIN rink.session AS s ON s.session_id = busy.session_id
CROSS JOIN (VALUES ('S'), ('L')) AS z (size)
ORDER BY s.session_id, z.size;
