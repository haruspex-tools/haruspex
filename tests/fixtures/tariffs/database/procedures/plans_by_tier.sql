-- The plans of one tier, chosen by a flag: either text the call may send matches the declaration whose {tier} stands for a
-- value of the query.
CREATE PROCEDURE billing.plans_by_tier @plus bit AS
DECLARE @response nvarchar(max),
    @url nvarchar(200) = N'https://billing.example/plans?tier=' + IIF(@plus = 1, N'plus', N'basic');
EXEC sys.sp_invoke_external_rest_endpoint @url = @url, @method = N'GET', @response = @response OUTPUT;
SELECT p.id, p.name
FROM OPENJSON(@response, '$.result') WITH (id int '$.id', name nvarchar(40) '$.name') AS p;
