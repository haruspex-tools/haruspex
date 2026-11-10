-- Width and height are both plain int, so a VALUES list of the two keeps their type.
CREATE TABLE mart.product (
    product_id int NOT NULL CONSTRAINT pk_product PRIMARY KEY,
    sku varchar(20) NOT NULL CONSTRAINT uq_product_sku UNIQUE,
    title nvarchar(80) NOT NULL,
    colour nvarchar(20) NULL,
    width_mm int NULL,
    height_mm int NULL
);
