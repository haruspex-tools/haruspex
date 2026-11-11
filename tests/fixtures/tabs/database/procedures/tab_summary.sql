-- One tab by key with its totals from an inline function applied per row: at most one row. COALESCE and ISNULL with a
-- value never null are never null; a money total converted to decimal(19,2) may overflow (NULL only under a caller's
-- ANSI_WARNINGS and ARITHABORT off); dates written with styles 23 and 120 are strings of the declared length.
CREATE PROCEDURE cafe.tab_summary
    @tab_id int
AS
SET NOCOUNT ON;

SELECT t.tab_id,
       g.name,
       COALESCE(g.nickname, g.name) AS display_name,
       ISNULL(g.phone, 'no phone') AS phone,
       s.items,
       s.subtotal,
       CONVERT(decimal(19, 2), s.subtotal) AS subtotal_2dp,
       COALESCE(s.discounts, 0) AS discounts,
       CONVERT(decimal(19, 2), s.subtotal) - ISNULL(CONVERT(decimal(19, 2), s.discounts), 0) AS due,
       CONVERT(varchar(10), t.opened_at, 23) AS opened_on,
       CONVERT(varchar(19), t.closed_at, 120) AS closed_text
FROM cafe.tab AS t
JOIN cafe.guest AS g ON g.guest_id = t.guest_id
CROSS APPLY cafe.tab_totals(t.tab_id) AS s
WHERE t.tab_id = @tab_id;
