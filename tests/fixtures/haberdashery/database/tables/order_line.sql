-- A line of an order. It shares order_id with shop.orders, so a star over both repeats the name.
CREATE TABLE shop.order_line (
    line_id int NOT NULL CONSTRAINT pk_order_line PRIMARY KEY,
    order_id int NOT NULL CONSTRAINT fk_order_line_orders REFERENCES shop.orders (order_id),
    sku varchar(12) NOT NULL,
    qty int NULL
);
