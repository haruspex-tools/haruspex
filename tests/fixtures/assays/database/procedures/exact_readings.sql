-- No column rounds a decimal to fewer places as NUMERIC_ROUNDABORT sees it: each keeps every place, or truncates or rounds
-- where the option doesn't look (to an integer, to money, from a float, ROUND itself, money's own arithmetic). None may be
-- NULL for a loss of precision, whatever the caller's session.
CREATE PROCEDURE assay.exact_readings
AS
SET NOCOUNT ON;
SELECT s.sample_id,
       CAST(s.dilution AS decimal(12,4)) AS dilution_4dp,
       s.dilution + s.dilution AS doubled,
       CAST(1.25 AS decimal(10,2)) AS standard,
       CAST('1.2' AS decimal(10,2)) AS text_standard,
       CAST(s.concentration AS int) AS whole_part,
       CAST(s.concentration AS money) AS concentration_money,
       CAST(s.absorbance AS decimal(10,2)) AS absorbance_2dp,
       CAST(s.fee AS decimal(19,4)) AS fee_4dp,
       ROUND(s.concentration, 2) AS concentration_rounded,
       s.concentration * s.concentration AS concentration_squared,
       s.dilution / s.dilution AS unit,
       s.fee * s.fee AS fee_squared,
       s.concentration % 1 AS fraction
FROM assay.sample AS s
ORDER BY s.sample_id;

SELECT s.sample_id, SUM(s.concentration) AS total, AVG(s.fee) AS mean_fee
FROM assay.sample AS s
GROUP BY s.sample_id
ORDER BY s.sample_id;
