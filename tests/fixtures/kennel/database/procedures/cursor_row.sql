-- A keyset cursor's row read where it is fetched: the row may have changed since OPEN, so its query's WHERE proves
-- nothing of it, and a variable its query reads holds what it held when the cursor was declared.
CREATE PROCEDURE kennel.cursor_row @dog_id int
AS
SET NOCOUNT ON;

DECLARE @chip varchar(15) = '-', @floor int = NULL, @fetched int = 0;
DECLARE dogs CURSOR LOCAL KEYSET FOR
    SELECT d.chip, @floor AS floor
    FROM kennel.dog AS d
    WHERE d.chip IS NOT NULL
    ORDER BY d.dog_id;
SET @floor = 2;

OPEN dogs;
UPDATE kennel.dog SET chip = NULL WHERE dog_id = @dog_id;
FETCH NEXT FROM dogs INTO @chip, @fetched;
CLOSE dogs;
DEALLOCATE dogs;

SELECT @chip AS chip, @fetched AS floor;
