-- One table, its whole key selected, no join: the view keeps the table's key, so a filter on purchase_id fixes it.
-- Schema-bound, with an index on it, so it can be read WITH (NOEXPAND).
CREATE VIEW mart.open_purchase
WITH SCHEMABINDING
AS
SELECT p.purchase_id, p.customer_id, p.placed_on, p.note
FROM mart.purchase AS p
WHERE p.status = 'open';
GO

CREATE UNIQUE CLUSTERED INDEX ix_open_purchase ON mart.open_purchase (purchase_id);
