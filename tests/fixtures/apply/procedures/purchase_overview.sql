-- A view with a join and both APPLYs inside it, read whole.
CREATE PROCEDURE mart.purchase_overview
AS
SET NOCOUNT ON;

SELECT s.purchase_id,
       s.customer_name,
       s.placed_on,
       s.line_count,
       s.first_sku
FROM mart.purchase_summary AS s
ORDER BY s.purchase_id;
