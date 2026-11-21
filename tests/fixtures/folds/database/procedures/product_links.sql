-- Each product's tag list, and the catalogue's while there are products, as one column: a UNION over JSON built from rows is text in every row.
CREATE PROCEDURE store.product_links
AS
SET NOCOUNT ON;

SELECT u.product_id, u.links
FROM (SELECT p.product_id, JSON_QUERY((SELECT t.name FROM store.tag AS t WHERE t.product_id = p.product_id ORDER BY t.tag_id FOR JSON PATH)) AS links
      FROM store.product AS p
      UNION ALL
      SELECT 0, JSON_QUERY((SELECT 'catalogue' AS name FOR JSON PATH)) WHERE EXISTS (SELECT 1 FROM store.product)) AS u
ORDER BY u.product_id
FOR JSON PATH;
