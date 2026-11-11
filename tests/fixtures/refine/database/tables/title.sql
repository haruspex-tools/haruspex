-- isbn is unique and nullable: SQL Server lets one row only have no ISBN, so `isbn IS NULL` fixes at most one row too.
CREATE TABLE lend.title (
    title_id int NOT NULL CONSTRAINT pk_title PRIMARY KEY,
    isbn varchar(13) NULL CONSTRAINT uq_title_isbn UNIQUE,
    name nvarchar(60) NOT NULL,
    author nvarchar(60) NULL,
    genre varchar(10) NULL CONSTRAINT ck_title_genre CHECK (genre IN ('fiction', 'history', 'science'))
);
