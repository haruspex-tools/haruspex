CREATE PROCEDURE meter.running_totals
    @meter_id int
AS
SELECT r.read_on, YEAR(r.read_on) AS read_year, r.kwh * 1000 AS wh, CAST(r.kwh AS smallint) AS kwh_small,
       SUM(r.kwh) OVER (ORDER BY r.read_on ROWS UNBOUNDED PRECEDING) AS running_kwh,
       COUNT(*) OVER () AS readings
FROM meter.reading AS r
WHERE r.meter_id = @meter_id;
