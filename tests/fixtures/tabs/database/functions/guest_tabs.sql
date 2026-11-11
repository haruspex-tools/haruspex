CREATE FUNCTION cafe.guest_tabs (@guest_id int)
RETURNS TABLE
AS
RETURN
SELECT t.tab_id, t.opened_at, t.closed_at
FROM cafe.tab AS t
WHERE t.guest_id = @guest_id;
