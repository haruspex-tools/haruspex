-- A cursor's query takes its variables as they were when it was declared: @floor is @start there, though set to 5 before
-- OPEN, so @seen is @start after the FETCH, null when it is (review round 1, finding 2).
CREATE PROCEDURE depot.low_floor
    @start int = NULL
AS
SET NOCOUNT ON;

DECLARE @floor int = @start, @part_id int = 0, @seen int = 0;
DECLARE low CURSOR LOCAL STATIC FOR SELECT s.part_id, @floor AS floor FROM depot.stock AS s ORDER BY s.part_id;
SET @floor = 5;
OPEN low;
FETCH NEXT FROM low INTO @part_id, @seen;
CLOSE low;
DEALLOCATE low;

SELECT @part_id AS part_id, @seen AS floor;
