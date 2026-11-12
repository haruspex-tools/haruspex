CREATE PROCEDURE garden.list_plants
AS
SELECT p.plant_id, p.name, p.kind, p.planted
FROM garden.plant AS p
ORDER BY p.name;
