-- A caller of the dynamic-SQL procedure, whose declared results flow into this one's.
CREATE PROCEDURE calls.search_with_ovens @product nvarchar(30) AS
SET NOCOUNT ON;
EXEC calls.oven_list;
EXEC calls.search_batches @product;
