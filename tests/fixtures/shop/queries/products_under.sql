-- Products at or under a price, cheapest first, a page at a time; every status when @status is NULL. Its one set is named,
-- so its function returns { products }.
-- @sets products
SELECT p.product_id, p.sku, p.name, p.price
FROM shop.product AS p
WHERE p.price <= @max_price AND (p.status = @status OR @status IS NULL)
ORDER BY p.price, p.product_id
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
