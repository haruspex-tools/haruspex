CREATE SCHEMA library;
GO
CREATE TABLE library.book (
    book_id int NOT NULL CONSTRAINT pk_book PRIMARY KEY,
    title nvarchar(200) NOT NULL,
    shelf_mark varchar(12) NULL
);
GO
CREATE VIEW library.shelf AS SELECT b.book_id, b.shelf_mark AS mark FROM library.book AS b;
GO
CREATE PROCEDURE library.find_book @title nvarchar(200) AS
SELECT b.book_id, b.title, UPPER(b.title) AS loud, b.shelf_mark FROM library.book AS b WHERE b.title = @title;
GO
EXEC sp_addextendedproperty N'MS_Description', N'A book the library holds.', N'SCHEMA', N'library', N'TABLE', N'book';
GO
EXEC sp_addextendedproperty N'MS_Description', N'The book''s title, as printed. It may hold */ and "quotes".', N'SCHEMA', N'library', N'TABLE', N'book', N'COLUMN', N'title';
GO
EXEC sp_addextendedproperty N'MS_Description', N'Where the book is shelved.
Two lines.', N'SCHEMA', N'library', N'TABLE', N'book', N'COLUMN', N'shelf_mark';
GO
EXEC sp_addextendedproperty N'Caption', N'Book', N'SCHEMA', N'library', N'TABLE', N'book';
GO
EXEC sp_addextendedproperty N'MS_Description', N'The books on the shelves.', N'SCHEMA', N'library', N'VIEW', N'shelf';
GO
EXEC sp_addextendedproperty N'MS_Description', N'The shelf mark, as the view names it.', N'SCHEMA', N'library', N'VIEW', N'shelf', N'COLUMN', N'mark';
GO
EXEC sp_addextendedproperty N'MS_Description', N'Finds a book by its title.', N'SCHEMA', N'library', N'PROCEDURE', N'find_book';
GO
EXEC sp_addextendedproperty N'MS_Description', N'The title to find.', N'SCHEMA', N'library', N'PROCEDURE', N'find_book', N'PARAMETER', N'@title';
GO
EXEC sp_addextendedproperty N'MS_Description', N'The primary key.', N'SCHEMA', N'library', N'TABLE', N'book', N'CONSTRAINT', N'pk_book';
GO
EXEC sp_addextendedproperty N'MS_Description', N'Books and shelves.', N'SCHEMA', N'library';
GO
