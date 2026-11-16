-- Strings: a name to search for, a fixed-length code, and a label in the database's code page.
CREATE PROCEDURE till.find_products
    @name nvarchar(40),
    @sku char(8) = NULL,
    @label varchar(20) = NULL
AS
SELECT p.product_id, p.sku, p.name, p.label
FROM till.product AS p
WHERE p.name LIKE @name + N'%'
  AND (@sku IS NULL OR p.sku = @sku)
  AND (@label IS NULL OR p.label = @label)
ORDER BY p.product_id;
