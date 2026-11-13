-- A table variable and a temporary table with the same name after the sigil are two tables: @tag's code is char(3) and
-- never null, #tag's varchar(10) and nullable. label is #tag's only, so it needs no qualifier.
CREATE PROCEDURE lib.variable_and_temp_of_one_name
AS
SET NOCOUNT ON;

DECLARE @tag TABLE (code char(3) NOT NULL);
CREATE TABLE #tag (code varchar(10) NULL, label nvarchar(20) NOT NULL);

INSERT INTO @tag (code) VALUES ('new'), ('old');
INSERT INTO #tag (code, label) VALUES ('new', N'Newly added'), (NULL, N'Unsorted');

SELECT v.code AS variable_code, t.code AS temp_code, label
FROM @tag AS v
CROSS JOIN #tag AS t
ORDER BY v.code, t.label;
