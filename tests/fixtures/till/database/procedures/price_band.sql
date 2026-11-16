-- Exact numbers: decimal bounds, and money and smallmoney, which hold four places.
CREATE PROCEDURE till.price_band
    @lowest decimal(9,2),
    @highest decimal(9,2) = NULL,
    @rate money = 1,
    @fee smallmoney = 0
AS
SELECT p.product_id, p.price, CAST(p.price * @rate + @fee AS decimal(19,4)) AS charged
FROM till.product AS p
WHERE p.price >= @lowest AND (@highest IS NULL OR p.price <= @highest)
ORDER BY p.product_id;
