-- The optional-filter search: each parameter, when NULL, leaves its column unfiltered, so no column is narrowed by it.
CREATE PROCEDURE lend.search_titles
    @name nvarchar(60) = NULL,
    @author nvarchar(60) = NULL,
    @genre varchar(10) = NULL
AS
SET NOCOUNT ON;

SELECT t.title_id, t.name, t.author, t.genre, t.isbn
FROM lend.title AS t
WHERE (@name IS NULL OR t.name LIKE @name + N'%')
  AND (@author IS NULL OR t.author = @author)
  AND (t.genre = @genre OR @genre IS NULL)
ORDER BY t.title_id;
