CREATE PROCEDURE library.on_shelf
AS
SELECT s.book_id, s.title, s.mark, d.mark AS same_mark
FROM library.shelf AS s
CROSS APPLY (SELECT s.mark) AS d (mark);
