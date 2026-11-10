-- Two levels of APPLY: customers, their purchases, each purchase's lines. A customer without purchases has no purchases
-- key (FOR JSON leaves out a null); a purchase without lines has no lines key.
CREATE PROCEDURE mart.customer_tree
AS
SET NOCOUNT ON;

SELECT c.customer_id AS id,
       c.name,
       p.purchases
FROM mart.customer AS c
OUTER APPLY (
    SELECT JSON_QUERY((
        SELECT pu.purchase_id AS id,
               pu.placed_on,
               l.lines
        FROM mart.purchase AS pu
        OUTER APPLY (
            SELECT JSON_QUERY((
                SELECT pl.line_no, pl.quantity
                FROM mart.purchase_line AS pl
                WHERE pl.purchase_id = pu.purchase_id
                ORDER BY pl.line_no
                FOR JSON PATH
            )) AS lines
        ) AS l
        WHERE pu.customer_id = c.customer_id
        ORDER BY pu.purchase_id
        FOR JSON PATH
    )) AS purchases
) AS p
ORDER BY c.customer_id
FOR JSON PATH;
