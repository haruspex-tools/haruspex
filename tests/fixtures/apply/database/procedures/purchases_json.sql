-- The same payload as one JSON document, the customer nested by path and the lines live JSON.
CREATE PROCEDURE mart.purchases_json
AS
SET NOCOUNT ON;

SELECT p.purchase_id AS id,
       p.status,
       c.name AS [customer.name],
       c.email AS [customer.email],
       l.lines
FROM mart.purchase AS p
JOIN mart.customer AS c ON c.customer_id = p.customer_id
CROSS APPLY (
    SELECT JSON_QUERY((
        SELECT pl.line_no AS [no], pl.quantity
        FROM mart.purchase_line AS pl
        WHERE pl.purchase_id = p.purchase_id
        ORDER BY pl.line_no
        FOR JSON PATH
    )) AS lines
) AS l
ORDER BY p.purchase_id
FOR JSON PATH;
