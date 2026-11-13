-- COALESCE of JSON built here, under json_query: live JSON of either's shape, whether or not the server folds it.
CREATE PROCEDURE kennel.coalesce_under_json_query @owner_id int
AS
DECLARE @dogs nvarchar(max) = (SELECT d.dog_id, d.name FROM kennel.dog AS d WHERE d.owner_id = @owner_id FOR JSON PATH);
DECLARE @none nvarchar(max) = N'[]';

SELECT json_query(COALESCE(@dogs, @none)) AS dogs
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
