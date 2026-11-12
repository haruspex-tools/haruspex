CREATE PROCEDURE garden.get_plant @plant_id int
AS
SELECT p.plant_id AS id,
       p.name,
       p.kind,
       (SELECT w.watered_at, w.litres
        FROM garden.water_log AS w
        WHERE w.plant_id = p.plant_id
        ORDER BY w.watered_at
        FOR JSON PATH) AS water_log
FROM garden.plant AS p
WHERE p.plant_id = @plant_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
