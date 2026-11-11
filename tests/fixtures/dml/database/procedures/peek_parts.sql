-- FETCH without INTO returns the row with a hidden ROWSTAT column some drivers list: the results are unknown (hidden-column).
CREATE PROCEDURE depot.peek_parts
AS
SET NOCOUNT ON;

DECLARE peek CURSOR LOCAL STATIC FOR SELECT p.part_id, p.name FROM depot.part AS p ORDER BY p.part_id;
OPEN peek;
FETCH NEXT FROM peek;
CLOSE peek;
DEALLOCATE peek;
