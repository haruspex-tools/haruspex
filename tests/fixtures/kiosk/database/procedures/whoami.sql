-- The deployment's region as literals: unknown, since the text written is not what the server runs.
CREATE PROCEDURE kiosk.whoami
AS
SELECT '$(Region)' AS region,
       N'$(Region)' AS region_n,
       'kiosk-$(Region)' AS host,
       len('$(Region)') AS region_length,
       'shared' AS fallback;
GO
