-- An order, as a customer placed it. Its note is the one column that may be null.
CREATE TABLE shop.orders (
    order_id int NOT NULL CONSTRAINT pk_orders PRIMARY KEY,
    customer nvarchar(40) NOT NULL,
    placed_on date NOT NULL,
    note nvarchar(200) NULL
);
