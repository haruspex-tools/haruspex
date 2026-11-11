-- Dynamic SQL: its results are declared in haruspex.json.
CREATE PROCEDURE calls.search_batches @product nvarchar(30) AS
SET NOCOUNT ON;
DECLARE @sql nvarchar(max) = N'SELECT b.batch_id, b.product, b.weight_g FROM calls.batch AS b WHERE b.product LIKE @p ORDER BY b.batch_id;';
EXEC sp_executesql @sql, N'@p nvarchar(30)', @p = @product;
