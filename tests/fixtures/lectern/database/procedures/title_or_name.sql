CREATE PROCEDURE library.title_or_name
    @reader bit
AS
IF @reader = 1
    SELECT r.name AS label FROM library.reader AS r;
ELSE
    SELECT b.title AS label FROM library.book AS b;
