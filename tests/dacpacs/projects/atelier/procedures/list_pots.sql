CREATE PROCEDURE studio.list_pots
    @kiln_id int = NULL,
    @count int OUTPUT
AS
SET NOCOUNT ON;
SELECT p.pot_id, p.note, k.glaze, studio.fahrenheit(k.peak) AS hot
FROM studio.pot AS p
JOIN studio.kiln AS k ON k.kiln_id = p.kiln_id
WHERE p.kiln_id = @kiln_id OR @kiln_id IS NULL;
SET @count = @@ROWCOUNT;
