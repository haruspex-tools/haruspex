CREATE TABLE nest.orders
(
    order_id    int          NOT NULL PRIMARY KEY,
    customer_id int          NOT NULL REFERENCES nest.customers (customer_id),
    placed_on   date         NOT NULL,
    note        nvarchar(20) NULL
);
GO
