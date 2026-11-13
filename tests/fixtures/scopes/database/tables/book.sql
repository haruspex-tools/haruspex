CREATE TABLE lib.book
(
    book_id  int          NOT NULL PRIMARY KEY,
    title    nvarchar(80) NOT NULL,
    pages    smallint     NULL,
    donor_id int          NULL REFERENCES lib.member (member_id)
);
