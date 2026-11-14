CREATE PROCEDURE meter.usage_summary
AS
SELECT m.meter_id, m.label + ' at ' + ISNULL(m.site, 'no site') AS described,
       COUNT(*) AS readings, SUM(r.kwh) AS total_kwh, AVG(r.kwh) AS mean_kwh, MAX(r.kwh) - MIN(r.kwh) AS spread_kwh,
       SUM(r.kwh) / COUNT(*) AS per_reading, CAST(MAX(r.peak_kw) AS decimal(5,1)) AS peak_kw
FROM meter.meter AS m
JOIN meter.reading AS r ON r.meter_id = m.meter_id
GROUP BY m.meter_id, m.label, m.site;
