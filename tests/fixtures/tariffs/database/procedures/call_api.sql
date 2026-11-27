-- A wrapper: every call of the billing service goes through it, with the path its caller passes. Its own results are unknown,
-- since an application may call it with any path; each procedure that calls it with a known path is typed at that call.
CREATE PROCEDURE billing.call_api @path nvarchar(200), @method nvarchar(6) = N'GET' AS
DECLARE @response nvarchar(max), @url nvarchar(400) = N'https://billing.example' + @path;
EXEC sys.sp_invoke_external_rest_endpoint @url = @url, @method = @method, @response = @response OUTPUT;
SELECT JSON_QUERY(@response, '$.result') AS body;
