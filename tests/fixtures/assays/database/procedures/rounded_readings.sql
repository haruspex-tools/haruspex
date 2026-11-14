-- Each column rounds a decimal to fewer places, so each is NULL for a caller with NUMERIC_ROUNDABORT on and ANSI_WARNINGS
-- and ARITHABORT off, and never null otherwise but where an overflow could be (precision-loss, arithmetic-overflow).
CREATE PROCEDURE assay.rounded_readings
AS
SET NOCOUNT ON;
DECLARE @reading decimal(10,2) = 1.2345;

-- Conversions to fewer places, values brought to decimal(38,0) beside the reference, arithmetic whose scale is cut past 38
-- digits, an average, LAG's default, an inline function's argument and an assignment.
SELECT s.sample_id,
       CAST(s.concentration AS decimal(10,2)) AS concentration_2dp,
       CONVERT(decimal(10,1), s.dilution) AS dilution_1dp,
       CAST(s.fee AS decimal(19,2)) AS fee_2dp,
       CAST(s.lab_value AS decimal(10,2)) AS lab_value_2dp,
       COALESCE(s.concentration, s.reference) AS coalesced,
       CASE WHEN s.sample_id > 0 THEN s.concentration ELSE s.reference END AS by_case,
       IIF(s.sample_id > 0, s.concentration, s.reference) AS by_iif,
       s.ratio * s.ratio AS ratio_squared,
       s.long_ratio / 3 AS long_ratio_third,
       s.fine_scale + s.big_count AS combined,
       AVG(s.concentration) OVER () AS mean_concentration,
       LAG(s.dilution, 1, s.concentration) OVER (ORDER BY s.sample_id) AS previous_dilution,
       t.level AS level_2dp,
       @reading AS reading
FROM assay.sample AS s
CROSS APPLY assay.at_two_places(s.concentration) AS t
ORDER BY s.sample_id;

-- A set operation's column: decimal(10,4) beside decimal(38,0) is decimal(38,0).
SELECT s.concentration AS level FROM assay.sample AS s
UNION ALL
SELECT s.reference FROM assay.sample AS s;

-- AVG of any decimal, grouped so that no group is empty.
SELECT s.sample_id, AVG(s.concentration) AS mean_concentration
FROM assay.sample AS s
GROUP BY s.sample_id
ORDER BY s.sample_id;
