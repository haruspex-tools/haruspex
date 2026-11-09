-- One part with its whole bill of materials.
CREATE PROCEDURE bom.get_assembly
    @part_id int
AS
SET NOCOUNT ON;

SELECT p.part_id AS id,
       p.name,
       p.kind,
       json_query(bom.components(p.part_id)) AS components
FROM bom.part AS p
WHERE p.part_id = @part_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
