CREATE PROCEDURE tailor.bands
AS
SELECT band FROM tailor.customer
UNION ALL
SELECT 4;
