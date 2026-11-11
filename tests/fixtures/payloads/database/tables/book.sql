CREATE TABLE pay.book
(
    book_id   int           NOT NULL PRIMARY KEY,
    author_id int           NOT NULL REFERENCES pay.author (author_id),
    title     nvarchar(60)  NOT NULL,
    isbn      char(13)      NULL,
    price     decimal(9, 2) NOT NULL CONSTRAINT ck_book_price CHECK (price >= 0),
    published date          NULL
);
GO
