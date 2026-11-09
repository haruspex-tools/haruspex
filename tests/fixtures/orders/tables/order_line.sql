CREATE TABLE sales.order_line
(
    -- Not declared NOT NULL, but the table's primary key makes it so.
    order_line_id  int,
    order_id       int            NOT NULL CONSTRAINT fk_order_line_order REFERENCES sales.[order] (order_id),
    product_id     int            NOT NULL CONSTRAINT fk_order_line_product REFERENCES sales.product (product_id),
    quantity       smallint       NOT NULL,
    unit_price     decimal(19, 2) NOT NULL,
    note           nvarchar(200)  NULL,
    CONSTRAINT pk_order_line PRIMARY KEY (order_line_id)
);
