CREATE TABLE lib.loan
(
    loan_id     int  NOT NULL IDENTITY PRIMARY KEY,
    member_id   int  NOT NULL REFERENCES lib.member (member_id),
    book_id     int  NOT NULL REFERENCES lib.book (book_id),
    loaned_on   date NOT NULL,
    returned_on date NULL
);
