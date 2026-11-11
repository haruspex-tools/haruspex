-- Tops up every part that is low, one at a time with a cursor, and returns the last part topped up. Each UPDATE fires the
-- audit trigger, which returns nothing. FETCH INTO may not assign (no low part), so the variables stay nullable.
CREATE PROCEDURE depot.restock
AS
SET NOCOUNT ON;

DECLARE @part_id int, @qty int, @shelf nvarchar(10);

DECLARE low CURSOR LOCAL FAST_FORWARD FOR
    SELECT s.part_id, s.qty, s.shelf FROM depot.stock AS s WHERE s.qty < 5 ORDER BY s.part_id;

OPEN low;
FETCH NEXT FROM low INTO @part_id, @qty, @shelf;
WHILE @@FETCH_STATUS = 0
BEGIN
    UPDATE depot.stock SET qty = qty + 10 WHERE part_id = @part_id;
    FETCH NEXT FROM low INTO @part_id, @qty, @shelf;
END
CLOSE low;
DEALLOCATE low;

SELECT @part_id AS last_part, @qty AS last_qty, @shelf AS last_shelf;
