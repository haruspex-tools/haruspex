-- A temporary table made by CREATE TABLE … AS SELECT, which SQL Server 2025 takes with a WITH clause: its columns are the
-- query's, as SELECT … INTO makes them. A part that is no assembly has a null component through the left join; the check
-- on unit and on quantity survive the table.
CREATE PROCEDURE flow.part_counts
AS
SET NOCOUNT ON;

CREATE TABLE #counts WITH (DISTRIBUTION = ROUND_ROBIN)
AS SELECT p.part_id, p.unit, c.part_id AS component_id, c.quantity
   FROM flow.part AS p
   LEFT JOIN flow.component AS c ON c.assembly_id = p.part_id;

SELECT x.part_id, x.unit, x.component_id, x.quantity FROM #counts AS x ORDER BY x.part_id, x.component_id;
