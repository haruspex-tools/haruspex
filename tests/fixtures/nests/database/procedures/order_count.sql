-- An aggregate without GROUP BY returns exactly one row, so the ROOT document is always written, over no orders too.
CREATE PROCEDURE nest.order_count
AS
SET NOCOUNT ON;

SELECT COUNT(*) AS orders
FROM nest.orders AS o
FOR JSON PATH, ROOT('summary');
