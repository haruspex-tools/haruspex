-- Each session as JSON with the people booked on it: a FOR JSON subquery over a derived table that is a UNION ALL (SQL Server
-- refuses FOR JSON on the set operation itself in a subquery); then everyone as one FOR JSON document over a UNION ALL, its
-- second column nested by its alias.
CREATE PROCEDURE rink.session_board
AS
SET NOCOUNT ON;

SELECT s.session_id AS id, s.kind,
       (SELECT p.name, p.skates
        FROM (SELECT m.name, b.skates
              FROM rink.booking AS b
              JOIN rink.member AS m ON m.member_id = b.member_id
              WHERE b.session_id = s.session_id
              UNION ALL
              SELECT g.name, b.skates
              FROM rink.booking AS b
              JOIN rink.guest AS g ON g.guest_id = b.guest_id
              WHERE b.session_id = s.session_id) AS p
        ORDER BY p.name
        FOR JSON PATH) AS people
FROM rink.session AS s
ORDER BY s.session_id
FOR JSON PATH;

SELECT m.member_id AS id, m.email AS [reach.contact]
FROM rink.member AS m
UNION ALL
SELECT g.guest_id, g.phone
FROM rink.guest AS g
ORDER BY id
FOR JSON PATH;
