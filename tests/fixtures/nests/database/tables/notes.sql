CREATE TABLE nest.notes
(
    note_id  int          NOT NULL PRIMARY KEY,
    order_id int          NOT NULL REFERENCES nest.orders (order_id),
    body     nvarchar(20) NULL
);
GO
