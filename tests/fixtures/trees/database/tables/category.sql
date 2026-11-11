CREATE TABLE cat.category
(
    category_id int          NOT NULL PRIMARY KEY,
    parent_id   int          NULL REFERENCES cat.category (category_id),
    name        nvarchar(20) NOT NULL
);
GO
