CREATE PROCEDURE sales.list_orders
    @customer_id int = NULL
AS
SET NOCOUNT ON;

SELECT o.order_id,
       o.placed_at,
       o.status,
       o.total,
       c.name AS customer_name,
       -- One row per order line; an order without lines still appears, through the left joins.
       p.legacy_code AS product_legacy_code,
       -- display_name and hired_on are NOT NULL, but the left join makes them nullable here.
       sp.display_name AS salesperson_name,
       sp.hired_on AS salesperson_hired_on
FROM sales.[order] AS o
INNER JOIN sales.customer AS c ON c.customer_id = o.customer_id
LEFT JOIN sales.order_line AS l ON l.order_id = o.order_id
LEFT JOIN sales.product AS p ON p.product_id = l.product_id
LEFT OUTER JOIN hr.salesperson sp ON sp.salesperson_id = o.salesperson_id
WHERE @customer_id IS NULL OR o.customer_id = @customer_id;
