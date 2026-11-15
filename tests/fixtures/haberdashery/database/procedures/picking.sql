-- A temporary table read by SELECT * before and after a column is added, and a table variable read the same way.
CREATE PROCEDURE shop.picking
AS
SET NOCOUNT ON;

SELECT * INTO #picked FROM shop.order_line AS l WHERE l.qty IS NOT NULL;
SELECT * FROM #picked ORDER BY line_id;

ALTER TABLE #picked ADD packed bit NULL;
INSERT INTO #picked (line_id, order_id, sku, qty, packed)
SELECT l.line_id + 1000, l.order_id, l.sku, l.qty, 1 FROM shop.order_line AS l WHERE l.qty > 1;
SELECT * FROM #picked ORDER BY line_id;

DECLARE @short TABLE (sku varchar(12) NOT NULL, qty int NULL);
INSERT INTO @short SELECT l.sku, l.qty FROM shop.order_line AS l WHERE l.qty IS NULL OR l.qty < 2;
SELECT * FROM @short ORDER BY sku;
