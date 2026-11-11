CREATE TABLE cat.item
(
    item_id         int          NOT NULL PRIMARY KEY,
    category_id     int          NOT NULL REFERENCES cat.category (category_id),
    title           nvarchar(20) NOT NULL,
    note            nvarchar(20) NULL,
    related_item_id int          NULL REFERENCES cat.item (item_id)
);
GO
