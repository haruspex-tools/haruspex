-- TRY_CAST turns the error NUMERIC_ROUNDABORT raises into NULL, so a rounding it attempts is NULL whatever ANSI_WARNINGS and
-- ARITHABORT are; one to more places never is.
CREATE PROCEDURE assay.attempted_readings
AS
SET NOCOUNT ON;
SELECT s.sample_id,
       TRY_CAST(s.concentration AS decimal(10,2)) AS attempted_2dp,
       TRY_CAST(s.dilution AS decimal(12,4)) AS attempted_4dp
FROM assay.sample AS s
ORDER BY s.sample_id;
