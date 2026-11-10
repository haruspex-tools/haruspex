-- INTERSECT and EXCEPT: a lesson booking's member, which is NULL for a guest's booking, intersected with members' ids is
-- never null; members' emails except those of members who booked keep the left side's NULL; INTERSECT binds tighter than
-- UNION, so a guest's NULL phone stays; and an INTERSECT whose left side is one member is at most one row.
CREATE PROCEDURE rink.regulars
    @member_id int
AS
SET NOCOUNT ON;

SELECT b.member_id
FROM rink.booking AS b
JOIN rink.session AS s ON s.session_id = b.session_id
WHERE s.kind = 'lesson'
INTERSECT
SELECT m.member_id
FROM rink.member AS m
ORDER BY member_id;

SELECT m.email
FROM rink.member AS m
EXCEPT
SELECT m.email
FROM rink.member AS m
JOIN rink.booking AS b ON b.member_id = m.member_id
ORDER BY email;

SELECT g.phone AS contact
FROM rink.guest AS g
UNION
SELECT m.email
FROM rink.member AS m
INTERSECT
SELECT m.email
FROM rink.member AS m
WHERE m.email IS NOT NULL
ORDER BY contact;

SELECT m.member_id, m.name
FROM rink.member AS m
WHERE m.member_id = @member_id
INTERSECT
SELECT b.member_id, m.name
FROM rink.booking AS b
JOIN rink.member AS m ON m.member_id = b.member_id;
