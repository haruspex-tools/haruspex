-- The plants not watered since a given time, the longest unwatered first; only those of one kind when @kind is given.
-- @since is compared with an aggregate, from which its type isn't inferred, so it is declared:
-- @param @since datetime2(0)
SELECT p.plant_id, p.name, MAX(w.watered_at) AS last_watered
FROM garden.plant AS p
LEFT JOIN garden.water_log AS w ON w.plant_id = p.plant_id
WHERE p.kind = @kind OR @kind IS NULL
GROUP BY p.plant_id, p.name
HAVING MAX(w.watered_at) < @since OR MAX(w.watered_at) IS NULL
ORDER BY MAX(w.watered_at), p.name;
