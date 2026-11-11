CREATE PROCEDURE sales.order_report
AS
SET NOCOUNT ON;

SELECT o.order_id,
       upper(c.name) AS customer_name,
       o.total * 1.2 AS total_with_tax,
       json_query(o.notes) AS notes,
       c.nickname,
       p.picture,
       v.code AS voucher_code
FROM sales.[order] AS o
JOIN sales.customer AS c ON c.customer_id = o.customer_id
JOIN sales.order_line AS l ON l.order_id = o.order_id
JOIN sales.product AS p ON p.product_id = l.product_id
LEFT JOIN sales.voucher AS v ON v.order_id = o.order_id;
