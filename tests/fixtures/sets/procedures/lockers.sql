-- FULL OUTER JOIN: a member without a locker and a locker without a member each come back with the other side NULL; a
-- WHERE on the locker's side drops the members without one.
CREATE PROCEDURE rink.lockers
AS
SET NOCOUNT ON;

SELECT m.member_id, m.name, l.locker_no, l.size
FROM rink.member AS m
FULL OUTER JOIN rink.locker AS l ON l.member_id = m.member_id
ORDER BY m.member_id, l.locker_no;

SELECT l.locker_no, l.size, m.name
FROM rink.member AS m
FULL JOIN rink.locker AS l ON l.member_id = m.member_id
WHERE l.locker_no > 0
ORDER BY l.locker_no;

-- Two sides of at most one row each that do not match give two rows: a full join is never bounded by its sides.
SELECT m.member_id, l.locker_no
FROM (SELECT x.member_id FROM rink.member AS x WHERE x.member_id = 3) AS m
FULL JOIN (SELECT y.locker_no, y.member_id FROM rink.locker AS y WHERE y.locker_no = 2) AS l ON l.member_id = m.member_id
ORDER BY m.member_id;
