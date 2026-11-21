-- A product's tags as a JSON array built from rows, or the empty array: COALESCE over it is text wherever it nests.
CREATE VIEW store.product_card
AS
SELECT p.product_id,
       p.name,
       COALESCE((SELECT CONVERT(nvarchar(max), JSON_ARRAYAGG(t.name ORDER BY t.name)) FROM store.tag AS t WHERE t.product_id = p.product_id), N'[]') AS tags
FROM store.product AS p;
