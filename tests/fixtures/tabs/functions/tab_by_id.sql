CREATE FUNCTION cafe.tab_by_id (@tab_id int)
RETURNS TABLE
AS
RETURN
SELECT t.tab_id, t.guest_id, t.opened_at, t.closed_at, t.note
FROM cafe.tab AS t
WHERE t.tab_id = @tab_id;
