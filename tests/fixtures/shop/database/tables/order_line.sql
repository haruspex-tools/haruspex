CREATE TABLE shop.customer_order
(
    order_id     int           NOT NULL CONSTRAINT pk_customer_order PRIMARY KEY,
    customer_id  int           NOT NULL CONSTRAINT fk_customer_order_customer REFERENCES shop.customer (customer_id),
    placed_at    datetime2(0)  NOT NULL
);
GO
CREATE TABLE shop.order_line
(
    line_id     int           NOT NULL CONSTRAINT pk_order_line PRIMARY KEY,
    order_id    int           NOT NULL CONSTRAINT fk_order_line_order REFERENCES shop.customer_order (order_id),
    shipped_at  datetime2(0)  NULL,
    price       decimal(9,2)  NOT NULL
);
