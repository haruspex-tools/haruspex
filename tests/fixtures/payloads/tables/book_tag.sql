CREATE TABLE pay.book_tag
(
    book_id int         NOT NULL REFERENCES pay.book (book_id),
    tag     varchar(20) NOT NULL,
    weight  smallint    NULL CONSTRAINT ck_book_tag_weight CHECK (weight BETWEEN 1 AND 100),
    PRIMARY KEY (book_id, tag)
);
GO
