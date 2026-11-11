-- Accumulation in a table variable, one region per pass of a loop. What it returns is the table variable's
-- declaration, whatever the loop inserts.
CREATE PROCEDURE flow.region_sales
AS
SET NOCOUNT ON;

DECLARE @acc TABLE (
    region_id int NOT NULL,
    sale_id int NOT NULL PRIMARY KEY,
    amount decimal(9, 2) NOT NULL CHECK (amount >= 0),
    note nvarchar(40) NULL
);
DECLARE @region int;

SELECT @region = MIN(r.region_id) FROM flow.region AS r;

WHILE @region IS NOT NULL
BEGIN
    INSERT INTO @acc (region_id, sale_id, amount, note)
    SELECT s.region_id, s.sale_id, s.amount, s.note
    FROM flow.sale AS s
    WHERE s.region_id = @region;

    SET @region = (SELECT MIN(r.region_id) FROM flow.region AS r WHERE r.region_id > @region);
END

SELECT a.region_id, a.sale_id, a.amount, a.note
FROM @acc AS a
ORDER BY a.sale_id;
