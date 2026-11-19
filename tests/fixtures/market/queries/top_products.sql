-- @module shop
SELECT TOP (@take) p.product_id, p.name FROM shop.products AS p ORDER BY p.price DESC;
