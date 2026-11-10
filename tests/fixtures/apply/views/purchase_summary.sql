-- A join and both APPLYs inside a view: the count is one row for every purchase, the first SKU none for a purchase
-- without lines.
CREATE VIEW mart.purchase_summary
AS
SELECT p.purchase_id,
       c.name AS customer_name,
       p.placed_on,
       n.line_count,
       f.first_sku
FROM mart.purchase AS p
JOIN mart.customer AS c ON c.customer_id = p.customer_id
CROSS APPLY (SELECT COUNT_BIG(*) AS line_count FROM mart.purchase_line AS l WHERE l.purchase_id = p.purchase_id) AS n
OUTER APPLY (
    SELECT TOP (1) pr.sku AS first_sku
    FROM mart.purchase_line AS l
    JOIN mart.product AS pr ON pr.product_id = l.product_id
    WHERE l.purchase_id = p.purchase_id
    ORDER BY l.line_no
) AS f;
