-- The latest open tab of a guest, through a view: at most one row for each call.
CREATE FUNCTION cafe.guest_open_tab (@guest_id int)
RETURNS TABLE
AS
RETURN
SELECT TOP (1) ot.tab_id, ot.opened_at
FROM cafe.open_tab AS ot
WHERE ot.guest_id = @guest_id
ORDER BY ot.opened_at DESC, ot.tab_id DESC;
