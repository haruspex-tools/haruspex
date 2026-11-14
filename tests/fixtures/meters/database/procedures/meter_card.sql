CREATE PROCEDURE meter.meter_card
    @meter_id int
AS
SELECT (
    SELECT m.meter_id AS id, m.label,
           (SELECT COUNT(*) FROM meter.reading AS r WHERE r.meter_id = m.meter_id) AS readings,
           (SELECT r.read_on AS [on], r.kwh * 1000 AS wh FROM meter.reading AS r WHERE r.meter_id = m.meter_id ORDER BY r.read_on FOR JSON PATH) AS days
    FROM meter.meter AS m
    WHERE m.meter_id = @meter_id
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
) AS card;
