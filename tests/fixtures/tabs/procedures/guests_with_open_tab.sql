-- OUTER APPLY of a function that reads a view and returns TOP (1): a guest without an open tab gets nulls (outer-apply).
CREATE PROCEDURE cafe.guests_with_open_tab
AS
SET NOCOUNT ON;

SELECT g.guest_id, g.name, o.tab_id AS open_tab_id, o.opened_at
FROM cafe.guest AS g
OUTER APPLY cafe.guest_open_tab(g.guest_id) AS o
ORDER BY g.guest_id;
