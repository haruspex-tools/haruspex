-- Members and guests in one list: UNION ALL, named by its first query, typed over both; a literal label of each as an exact
-- set; a member's CHECKed level beside a guest's NULL literal.
CREATE PROCEDURE rink.everyone
AS
SET NOCOUNT ON;

SELECT m.member_id AS person_id, m.name, m.email AS contact, 'member' AS kind, m.level
FROM rink.member AS m
UNION ALL
SELECT g.guest_id, g.name, g.phone, 'guest', NULL
FROM rink.guest AS g
ORDER BY kind, person_id;
