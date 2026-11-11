-- Over one table with its whole key, but every version of it: no key passes through.
CREATE VIEW mart.price_all
AS
SELECT p.product_id, p.amount
FROM mart.price FOR SYSTEM_TIME ALL AS p;
GO

-- Keyed, until it is read FOR SYSTEM_TIME ALL itself.
CREATE VIEW mart.price_now
AS
SELECT p.product_id, p.amount
FROM mart.price AS p;
GO

-- At most one row because its WHERE fixes the key: read FOR SYSTEM_TIME ALL, one row per version.
CREATE VIEW mart.bowl_price
AS
SELECT p.product_id, p.amount
FROM mart.price AS p
WHERE p.product_id = 101;
