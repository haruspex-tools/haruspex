-- A product's reference as JSON only when it has one, and its first tag: a CASE on a table's column whose last result is NULL,
-- and NULLIF over JSON text, are text in FOR JSON and in json_object alike, as is COALESCE over
-- json_arrayagg whose last argument is a literal.
CREATE PROCEDURE store.product_refs
AS
SET NOCOUNT ON;

SELECT p.product_id,
       CASE WHEN p.ref IS NOT NULL THEN JSON_QUERY(JSON_OBJECT('system': p.ref_system, 'id': p.ref)) END AS ref,
       NULLIF((SELECT TOP (1) t.name FROM store.tag AS t WHERE t.product_id = p.product_id ORDER BY t.tag_id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N'') AS first_tag
FROM store.product AS p
ORDER BY p.product_id
FOR JSON PATH;

SELECT p.product_id,
       JSON_OBJECT('ref': CASE WHEN p.ref IS NOT NULL THEN JSON_OBJECT('id': p.ref) END,
                   'tags': COALESCE((SELECT JSON_ARRAYAGG(t.name) FROM store.tag AS t WHERE t.product_id = p.product_id), N'[]')) AS summary
FROM store.product AS p
ORDER BY p.product_id;
