-- The same idiom through a derived table: the last order picked, opened again. inserted.* lists the derived table's columns,
-- which are every column of depot.pick_order.
CREATE PROCEDURE depot.reopen_order
AS
SET NOCOUNT ON;

UPDATE p
SET status = 'open'
OUTPUT inserted.*
FROM (SELECT TOP (1) * FROM depot.pick_order WHERE status = 'picked' ORDER BY order_id DESC) AS p;
