CREATE TABLE shop.customer
(
    customer_id  int           NOT NULL CONSTRAINT pk_customer PRIMARY KEY,
    name         nvarchar(60)  NOT NULL
);
GO
CREATE TABLE shop.basket
(
    basket_id    int  NOT NULL CONSTRAINT pk_basket PRIMARY KEY,
    -- Nullable for guest checkout, which this application doesn't offer: the config file says so.
    customer_id  int  NULL CONSTRAINT fk_basket_customer REFERENCES shop.customer (customer_id)
);
GO
CREATE TABLE shop.basket_line
(
    basket_id   int  NOT NULL CONSTRAINT fk_basket_line_basket REFERENCES shop.basket (basket_id),
    line_no     int  NOT NULL,
    product_id  int  NOT NULL CONSTRAINT fk_basket_line_product REFERENCES shop.product (product_id),
    quantity    int  NOT NULL,
    CONSTRAINT pk_basket_line PRIMARY KEY (basket_id, line_no)
);
