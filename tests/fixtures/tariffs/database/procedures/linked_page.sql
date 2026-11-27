-- A URL read from data: whatever it names is not known here, so the body is unknown, saying why. The envelope is still typed.
CREATE PROCEDURE billing.linked_page @plan_id int AS
DECLARE @response nvarchar(max), @url nvarchar(400) = (SELECT l.href FROM billing.plan_link AS l WHERE l.plan_id = @plan_id);
EXEC sys.sp_invoke_external_rest_endpoint @url = @url, @method = N'GET', @response = @response OUTPUT;
SELECT JSON_VALUE(@response, '$.response.status.http.code') AS http_code, JSON_QUERY(@response, '$.result') AS body;
