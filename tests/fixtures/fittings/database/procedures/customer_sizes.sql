-- JSON computed from a masked column is its mask, which the server writes into the outer JSON without quotes: not JSON.
CREATE PROCEDURE tailor.customer_sizes
AS
SELECT o.customer_id,
       (SELECT i.size_code FROM tailor.customer AS i WHERE i.customer_id = o.customer_id FOR JSON PATH) AS sizes
FROM tailor.customer AS o
FOR JSON PATH;
