-- A receipt as one JSON object: string building with CONCAT (never null), CONCAT_WS and + (null when an operand is),
-- TRY_CAST (null when the note is not a number), and amounts cast to decimal(19,2), which they always fit.
CREATE PROCEDURE cafe.tab_receipt
    @tab_id int
AS
SET NOCOUNT ON;

SELECT t.tab_id AS [tab.id],
       CONCAT_WS(N' / ', g.name, g.nickname) AS [tab.guest],
       TRY_CAST(t.note AS int) AS [tab.note_number],
       (SELECT CONCAT(mi.title, N' x', CAST(ti.qty AS varchar(6))) AS line,
               mi.short_code + '-' + CAST(ti.tab_item_id AS varchar(11)) AS ref,
               CAST(ti.qty * mi.price AS decimal(19, 2)) AS amount,
               ti.discount
        FROM cafe.tab_item AS ti
        JOIN cafe.menu_item AS mi ON mi.item_id = ti.item_id
        WHERE ti.tab_id = t.tab_id
        ORDER BY ti.tab_item_id
        FOR JSON PATH) AS items
FROM cafe.tab AS t
JOIN cafe.guest AS g ON g.guest_id = t.guest_id
WHERE t.tab_id = @tab_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
