-- COALESCE of an aggregate's JSON assigned to a variable: the variable holds the same text whether or not the server folds the
-- COALESCE, so json_query reads the owner's dog ids, or json_array()'s empty array when the subquery finds none.
CREATE PROCEDURE kennel.coalesce_into_variable @owner_id int
AS
DECLARE @dog_ids nvarchar(max) = COALESCE((SELECT json_arrayagg(d.dog_id) FROM kennel.dog AS d WHERE d.owner_id = @owner_id), json_array());

SELECT json_query(@dog_ids) AS dog_ids;
