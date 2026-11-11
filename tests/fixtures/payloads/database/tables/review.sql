CREATE TABLE pay.review
(
    review_id int               NOT NULL PRIMARY KEY,
    book_id   int               NOT NULL REFERENCES pay.book (book_id),
    stars     tinyint           NOT NULL CONSTRAINT ck_review_stars CHECK (stars BETWEEN 1 AND 5),
    comment   nvarchar(100)     NULL,
    posted    datetimeoffset(0) NOT NULL
);
GO
