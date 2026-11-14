-- SET NUMERIC_ROUNDABORT OFF lasts while the procedure runs, so the rounding below is silent whatever the caller set. The
-- SET is passed over, as one that can only make a value less often NULL: the column keeps its reason, which claims less.
CREATE PROCEDURE assay.silent_rounding
AS
SET NOCOUNT ON;
SET NUMERIC_ROUNDABORT OFF;
SELECT s.sample_id, CAST(s.concentration AS decimal(10,2)) AS concentration_2dp
FROM assay.sample AS s
ORDER BY s.sample_id;
