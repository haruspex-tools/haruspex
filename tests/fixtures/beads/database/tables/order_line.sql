CREATE TABLE bead.order_line
(
    line_id  int          NOT NULL PRIMARY KEY,
    order_id int          NOT NULL REFERENCES bead.[order] (order_id),
    qty      smallint     NOT NULL,
    price    decimal(7,2) NOT NULL,
    gift     bit          NOT NULL
);
GO
