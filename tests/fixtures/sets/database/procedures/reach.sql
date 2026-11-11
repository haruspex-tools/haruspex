-- A view over UNION ALL (its key does not survive, so one id may be a member and a guest) and an inline function over UNION.
CREATE PROCEDURE rink.reach
    @person_id int,
    @session_id int
AS
SET NOCOUNT ON;

SELECT c.person_id, c.name, c.contact, c.kind
FROM rink.contacts AS c
WHERE c.person_id = @person_id
ORDER BY c.kind;

SELECT p.name, p.kind
FROM rink.session_people(@session_id) AS p
ORDER BY p.kind, p.name;
