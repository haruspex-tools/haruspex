CREATE TABLE library.book (
    book_id int NOT NULL CONSTRAINT pk_book PRIMARY KEY,
    title nvarchar(200) NOT NULL,
    shelf_mark nvarchar(12) NULL,
    pages int NULL
);
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'A book the reading room holds.', @level0type = N'SCHEMA', @level0name = N'library', @level1type = N'TABLE', @level1name = N'book';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'@deprecated is not a tag here: the catalogue''s own number, as on the spine.', @level0type = N'SCHEMA', @level0name = N'library', @level1type = N'TABLE', @level1name = N'book', @level2type = N'COLUMN', @level2name = N'book_id';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The title as printed, which may hold */ or "quotes" or a \ backslash.', @level0type = N'SCHEMA', @level0name = N'library', @level1type = N'TABLE', @level1name = N'book', @level2type = N'COLUMN', @level2name = N'title';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Where the book is shelved.
A case and a shelf, such as B-12.', @level0type = N'SCHEMA', @level0name = N'library', @level1type = N'TABLE', @level1name = N'book', @level2type = N'COLUMN', @level2name = N'shelf_mark';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'An index of the catalogue.', @level0type = N'SCHEMA', @level0name = N'library', @level1type = N'TABLE', @level1name = N'book', @level2type = N'CONSTRAINT', @level2name = N'pk_book';
GO
