-- Dynamic SQL whose results the call declares itself (WITH RESULT SETS), so they are typed without a declaration in
-- haruspex.json: the server enforces the declaration whatever the text runs.
CREATE PROCEDURE calls.search_declared @product nvarchar(30) AS
SET NOCOUNT ON;
DECLARE @sql nvarchar(max) = N'SELECT b.batch_id, b.product, b.weight_g FROM calls.batch AS b WHERE b.product LIKE @p ORDER BY b.batch_id;';
EXEC sp_executesql @sql, N'@p nvarchar(30)', @p = @product
WITH RESULT SETS ((batchId int NOT NULL, product nvarchar(30) NOT NULL, grams int NULL));
