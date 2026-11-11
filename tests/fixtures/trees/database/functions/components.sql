-- A bill of materials as a tree: each component of an assembly, with its own components, to any depth. A part with no
-- components (a bought-in part, or an assembly with none yet) gets NULL from FOR JSON over no rows, so its key is left
-- out. SQL Server stops at 32 nested calls (error 217), which the type does not express.
CREATE FUNCTION bom.components(@assembly_id int)
RETURNS nvarchar(max)
AS
BEGIN
    RETURN (
        SELECT c.quantity,
               p.part_id AS [part.id],
               p.name AS [part.name],
               p.kind AS [part.kind],
               p.unit_cost AS [part.unitCost],
               json_query(bom.components(p.part_id)) AS components
        FROM bom.component AS c
        JOIN bom.part AS p ON p.part_id = c.part_id
        WHERE c.assembly_id = @assembly_id
        ORDER BY p.part_id
        FOR JSON PATH);
END
GO
