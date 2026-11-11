CREATE TABLE mart.purchase_line (
    purchase_id int NOT NULL CONSTRAINT fk_line_purchase REFERENCES mart.purchase (purchase_id),
    line_no int NOT NULL,
    product_id int NOT NULL CONSTRAINT fk_line_product REFERENCES mart.product (product_id),
    quantity int NOT NULL,
    unit_price decimal(9, 2) NOT NULL,
    discount decimal(5, 2) NULL,
    CONSTRAINT pk_purchase_line PRIMARY KEY (purchase_id, line_no)
);
