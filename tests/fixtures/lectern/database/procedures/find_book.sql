CREATE PROCEDURE library.find_book
    @title nvarchar(200)
AS
SELECT b.book_id, b.title, UPPER(b.title) AS loud, b.shelf_mark, b.pages + 0 AS pages
FROM library.book AS b
WHERE b.title = @title;
GO
EXEC sp_addextendedproperty N'MS_Description', N'Finds a book by its exact title.', N'SCHEMA', N'library', N'PROCEDURE', N'find_book';
GO
EXEC sp_addextendedproperty N'MS_Description', N'The title to find, as printed.', N'SCHEMA', N'library', N'PROCEDURE', N'find_book', N'PARAMETER', N'@title';
GO
