-- An inline function whose column is built JSON.
CREATE FUNCTION kit.parts_of(@kit_id int)
RETURNS TABLE
AS
RETURN
    SELECT k.kit_id, (SELECT p.sku, p.colour FROM kit.kit_part AS p WHERE p.kit_id = k.kit_id ORDER BY p.part_id FOR JSON PATH) AS parts
    FROM kit.kit AS k
    WHERE k.kit_id = @kit_id;
