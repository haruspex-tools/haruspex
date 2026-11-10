-- Who is booked on a session, by name: an inline function whose query is a UNION.
CREATE FUNCTION rink.session_people (@session_id int)
RETURNS TABLE
AS
RETURN
    SELECT m.name, 'member' AS kind
    FROM rink.booking AS b
    JOIN rink.member AS m ON m.member_id = b.member_id
    WHERE b.session_id = @session_id
    UNION
    SELECT g.name, 'guest'
    FROM rink.booking AS b
    JOIN rink.guest AS g ON g.guest_id = b.guest_id
    WHERE b.session_id = @session_id;
