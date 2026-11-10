-- A temporary table created, filled, widened by ALTER TABLE and updated. Its columns are what their declarations
-- say: the check on unit, NOT NULL on name, and the added column's NULL.
CREATE PROCEDURE flow.part_list
    @with_units bit = 1
AS
SET NOCOUNT ON;

CREATE TABLE #p (
    part_id int NOT NULL PRIMARY KEY,
    name nvarchar(30) NOT NULL,
    unit varchar(4) NULL CHECK (unit IN ('each', 'kg', 'm'))
);

INSERT INTO #p (part_id, name, unit)
SELECT p.part_id, p.name, p.unit FROM flow.part AS p;

ALTER TABLE #p ADD used_in int NULL;

UPDATE x SET used_in = c.assembly_id
FROM #p AS x
JOIN flow.component AS c ON c.part_id = x.part_id;

IF @with_units = 0
    UPDATE #p SET unit = NULL;

BEGIN TRY
    SELECT x.part_id, x.name, x.unit, x.used_in FROM #p AS x ORDER BY x.part_id;
END TRY
BEGIN CATCH
    THROW;
END CATCH
