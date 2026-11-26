CREATE VIEW library.shelf AS
SELECT b.book_id, b.title, b.shelf_mark AS mark FROM library.book AS b WHERE b.shelf_mark IS NOT NULL;
GO
EXEC sp_addextendedproperty N'MS_Description', N'The shelf mark, as the shelf list names it.', N'SCHEMA', N'library', N'VIEW', N'shelf', N'COLUMN', N'mark';
GO
