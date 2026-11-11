-- Every sale through the view, as rows and as JSON.
CREATE PROCEDURE bazaar.sales_feed
AS
SET NOCOUNT ON;

SELECT v.kind, v.sale_id, v.sold_at, v.units, v.amount, v.method, v.memo
FROM bazaar.all_sales AS v
ORDER BY v.kind, v.sale_id;

SELECT (
    SELECT v.kind, v.sale_id, v.sold_at, v.units, v.amount, v.method, v.memo
    FROM bazaar.all_sales AS v
    ORDER BY v.kind, v.sale_id
    FOR JSON PATH, INCLUDE_NULL_VALUES
) AS feed;
