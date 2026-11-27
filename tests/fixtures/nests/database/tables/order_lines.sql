-- No foreign key to nest.orders: the seed holds a line whose order is gone, which a RIGHT JOIN keeps unmatched.
CREATE TABLE nest.order_lines
(
    line_id  int        NOT NULL PRIMARY KEY,
    order_id int        NOT NULL,
    sku      varchar(8) NOT NULL,
    qty      int        NOT NULL
);
GO
