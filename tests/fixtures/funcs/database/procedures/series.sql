CREATE PROCEDURE gym.series
    @upto int
AS
SELECT s.value AS n
FROM GENERATE_SERIES(1, @upto) AS s
ORDER BY s.value;

SELECT v.visit_id, s.value AS seat
FROM gym.visit AS v
CROSS APPLY GENERATE_SERIES(CAST(1 AS tinyint), v.room) AS s
ORDER BY v.visit_id, s.value;

SELECT value AS step
FROM GENERATE_SERIES(0.0, 1.0, 0.25)
ORDER BY value;
