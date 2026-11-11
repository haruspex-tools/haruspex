-- OPENJSON and JSON_VALUE over JSON built here: each type read back into itself, a string into a shorter type, a key FOR JSON
-- leaves out when null, read lax and strict; the keys and types of an object; scalars of a literal document.
CREATE PROCEDURE kit.part_rows @kit_id int
AS
SET NOCOUNT ON;
DECLARE @parts nvarchar(max) = (SELECT p.part_id, p.sku, p.qty, p.colour FROM kit.kit_part AS p WHERE p.kit_id = @kit_id ORDER BY p.part_id FOR JSON PATH);
DECLARE @card nvarchar(max) = (SELECT k.kit_id, k.name, k.price FROM kit.kit AS k WHERE k.kit_id = @kit_id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
DECLARE @style nvarchar(max) = N'{"finish": "matt", "sizes": [1, 2, 3], "boxed": false}';

SELECT j.part_id, j.sku, j.qty, j.qty_wide, j.sku_short, j.colour
FROM OPENJSON(@parts) WITH (part_id int, sku varchar(8), qty tinyint, qty_wide int '$.qty', sku_short nvarchar(3) '$.sku', colour nvarchar(10) 'lax $.colour') AS j
ORDER BY j.part_id;

SELECT j.[key], j.value, j.type FROM OPENJSON(@card) AS j ORDER BY j.[key];

SELECT json_value(@card, '$.name') AS name, json_value(@style, '$.finish') AS finish, json_value(@style, 'strict $.boxed') AS boxed,
       j.finish AS with_finish, j.sizes
FROM OPENJSON(@style) WITH (finish nvarchar(10), sizes nvarchar(max) AS JSON) AS j
WHERE @kit_id <> 99
FOR JSON PATH;
