-- JSON built into a table variable and a temporary table, written several ways, read back through json_query; and the
-- same JSON returned as rowset columns, which a client receives as text.
CREATE PROCEDURE kit.kit_sheets
AS
SET NOCOUNT ON;
DECLARE @sheets TABLE (kit_id int NOT NULL PRIMARY KEY, parts nvarchar(max) NULL, card nvarchar(max) NOT NULL);
CREATE TABLE #sheets (kit_id int NOT NULL PRIMARY KEY, parts nvarchar(max) NULL);

INSERT @sheets (kit_id, parts, card)
SELECT k.kit_id,
       (SELECT p.sku, p.qty FROM kit.kit_part AS p WHERE p.kit_id = k.kit_id ORDER BY p.part_id FOR JSON PATH),
       json_object('name': k.name)
FROM kit.kit AS k;

UPDATE @sheets SET parts = '[]' WHERE parts IS NULL AND kit_id = 2;

INSERT #sheets (kit_id, parts) SELECT s.kit_id, s.parts FROM @sheets AS s;
INSERT #sheets (kit_id, parts) VALUES (-1, '[{"sku": "SPARE", "qty": 0}]');

SELECT s.kit_id, json_query(s.parts) AS parts, json_query(s.card) AS card, json_query(t.parts) AS copied
FROM @sheets AS s
JOIN #sheets AS t ON t.kit_id = s.kit_id
ORDER BY s.kit_id
FOR JSON PATH;

SELECT t.kit_id, t.parts FROM #sheets AS t ORDER BY t.kit_id;
