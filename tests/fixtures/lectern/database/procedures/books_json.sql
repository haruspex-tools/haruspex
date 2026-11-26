CREATE PROCEDURE library.books_json
AS
SELECT b.book_id AS id, b.title AS [shelf.title], b.shelf_mark AS [shelf.mark], LEN(b.title) AS [shelf.length]
FROM library.book AS b
FOR JSON PATH;
