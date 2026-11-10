-- Records each change of quantity. It returns nothing, so writes to depot.stock stay typed.
CREATE TRIGGER depot.stock_audit ON depot.stock
AFTER INSERT, UPDATE
AS
SET NOCOUNT ON;

INSERT INTO depot.stock_move (part_id, delta)
SELECT i.part_id, i.qty - ISNULL(d.qty, 0)
FROM inserted AS i
LEFT JOIN deleted AS d ON d.part_id = i.part_id
WHERE i.qty <> ISNULL(d.qty, 0);
