-- Every part with its tree, and the same tree as text (a plain call is escaped, not nested).
CREATE PROCEDURE bom.list_parts
AS
SET NOCOUNT ON;

SELECT p.part_id AS id,
       p.name,
       json_query(bom.components(p.part_id)) AS components,
       bom.components(p.part_id) AS componentsText
FROM bom.part AS p
ORDER BY p.part_id
FOR JSON PATH;
