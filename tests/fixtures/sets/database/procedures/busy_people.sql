-- A set operation feeding INSERT … SELECT and SELECT … INTO.
CREATE PROCEDURE rink.busy_people
AS
SET NOCOUNT ON;

CREATE TABLE #busy (person_id int NOT NULL, kind varchar(6) NOT NULL);

INSERT INTO #busy (person_id, kind)
SELECT b.member_id, 'member' FROM rink.booking AS b WHERE b.member_id IS NOT NULL
UNION
SELECT b.guest_id, 'guest' FROM rink.booking AS b WHERE b.guest_id IS NOT NULL;

SELECT b.person_id, b.kind
FROM #busy AS b
ORDER BY b.kind, b.person_id;

SELECT s.session_id AS id, s.coach_id AS other
INTO #ids
FROM rink.session AS s
UNION ALL
SELECT l.locker_no, l.member_id
FROM rink.locker AS l;

SELECT i.id, i.other
FROM #ids AS i
ORDER BY i.id, i.other;
