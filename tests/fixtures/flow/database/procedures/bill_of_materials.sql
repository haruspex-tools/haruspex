-- A bill of materials: every part under an assembly, at any depth, by a recursive CTE. Two CTEs, the second
-- reading the first, and the quantity's check carried through both.
CREATE PROCEDURE flow.bill_of_materials
    @assembly int
AS
SET NOCOUNT ON;

WITH bom (assembly_id, part_id, quantity) AS (
    SELECT c.assembly_id, c.part_id, c.quantity
    FROM flow.component AS c
    WHERE c.assembly_id = @assembly
    UNION ALL
    SELECT c.assembly_id, c.part_id, c.quantity
    FROM flow.component AS c
    JOIN bom AS b ON c.assembly_id = b.part_id
),
named AS (
    SELECT bom.assembly_id, bom.quantity, p.part_id, p.name, p.unit
    FROM bom
    JOIN flow.part AS p ON p.part_id = bom.part_id
)
SELECT named.assembly_id AS assemblyId, named.part_id AS [part.id], named.name AS [part.name], named.unit AS [part.unit], named.quantity
FROM named
ORDER BY named.assembly_id, named.part_id
FOR JSON PATH;
