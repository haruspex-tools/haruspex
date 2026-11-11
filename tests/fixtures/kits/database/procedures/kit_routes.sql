-- Built JSON read through a view, an inline function, a multi-statement function, a CTE, a derived table and SELECT … INTO.
CREATE PROCEDURE kit.kit_routes @kit_id int
AS
SET NOCOUNT ON;
SELECT k.kit_id, (SELECT p.sku FROM kit.kit_part AS p WHERE p.kit_id = k.kit_id ORDER BY p.part_id FOR JSON PATH) AS parts, json_object('name': k.name) AS summary
INTO #cards
FROM kit.kit AS k;

WITH built AS (SELECT k.kit_id, json_object('price': k.price) AS price FROM kit.kit AS k)
SELECT c.kit_id,
       json_query(c.parts) AS view_parts,
       json_query(c.summary) AS view_summary,
       json_query(f.parts) AS inline_parts,
       json_query(s.parts) AS sheet_parts,
       b.price AS cte_price,
       json_query(d.card) AS derived_card,
       json_query(x.summary) AS into_summary
FROM kit.kit_card AS c
OUTER APPLY kit.parts_of(c.kit_id) AS f
OUTER APPLY kit.part_sheet(c.kit_id) AS s
JOIN built AS b ON b.kit_id = c.kit_id
JOIN (SELECT k.kit_id, json_object('id': k.kit_id) AS card FROM kit.kit AS k) AS d ON d.kit_id = c.kit_id
JOIN #cards AS x ON x.kit_id = c.kit_id
WHERE @kit_id IS NULL OR c.kit_id = @kit_id
ORDER BY c.kit_id
FOR JSON PATH;
