-- INSERT … EXEC staging into temporary tables, then FOR JSON. #stage is declared, so it holds its declaration; #sizes is
-- made by SELECT … INTO from a variable, so it holds what rack_sizes returns into it.
CREATE PROCEDURE calls.batch_feed @oven int = NULL AS
SET NOCOUNT ON;
CREATE TABLE #stage (id int NOT NULL, name nvarchar(30) NOT NULL, grams int NULL);
INSERT INTO #stage (id, name, grams) EXEC calls.batch_rows @oven;
DECLARE @none int = 0;
SELECT @none AS size INTO #sizes WHERE 1 = 0;
INSERT INTO #sizes (size) EXEC calls.rack_sizes;
SELECT s.id, s.name, s.grams FROM #stage AS s ORDER BY s.id FOR JSON PATH;
SELECT z.size FROM #sizes AS z ORDER BY z.size FOR JSON PATH;
