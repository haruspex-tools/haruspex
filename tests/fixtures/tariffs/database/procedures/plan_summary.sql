-- One plan, called directly, and read field by field: the envelope's status, never NULL once the call returns, and the
-- declared body's fields, each NULL where the reply may have no body.
CREATE PROCEDURE billing.plan_summary @plan_id int AS
DECLARE @response nvarchar(max), @status int,
    @url nvarchar(200) = CONCAT(N'https://billing.example/plans/', ISNULL(@plan_id, 0));
EXEC @status = sys.sp_invoke_external_rest_endpoint @url = @url, @method = N'GET', @response = @response OUTPUT;
SELECT @status AS status,
    JSON_VALUE(@response, '$.response.status.http.code') AS http_code,
    JSON_VALUE(@response, '$.result.name') AS name,
    JSON_VALUE(@response, '$.result.tier') AS tier;
