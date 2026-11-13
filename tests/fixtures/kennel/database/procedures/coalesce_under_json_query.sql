-- COALESCE of JSON built here, under json_query: live JSON of either's shape, whether or not the server folds it. And a
-- CASE over a FOR JSON subquery, which only json_query around it makes JSON whether or not the server folds the CASE.
CREATE PROCEDURE kennel.coalesce_under_json_query @owner_id int
AS
DECLARE @dogs nvarchar(max) = (SELECT d.dog_id, d.name FROM kennel.dog AS d WHERE d.owner_id = @owner_id FOR JSON PATH);
DECLARE @none nvarchar(max) = N'[]';

SELECT json_query(COALESCE(@dogs, @none)) AS dogs,
       json_query(CASE WHEN @owner_id > 0
                       THEN (SELECT d.dog_id, d.name FROM kennel.dog AS d WHERE d.owner_id = @owner_id FOR JSON PATH)
                       ELSE N'[]' END) AS dogs_by_case
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
