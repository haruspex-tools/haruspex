-- A multi-statement function whose return table it fills with built JSON, or an empty array when the kit has no parts; no
-- row for a kit given away free.
CREATE FUNCTION kit.part_sheet(@kit_id int)
RETURNS @sheet TABLE (kit_id int NOT NULL, parts nvarchar(max) NULL)
AS
BEGIN
    INSERT @sheet (kit_id, parts)
    SELECT k.kit_id, (SELECT p.sku, p.qty FROM kit.kit_part AS p WHERE p.kit_id = k.kit_id ORDER BY p.part_id FOR JSON PATH)
    FROM kit.kit AS k
    WHERE k.kit_id = @kit_id AND k.price > 0;

    UPDATE @sheet SET parts = '[]' WHERE parts IS NULL;
    RETURN;
END
