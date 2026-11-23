CREATE TABLE bake.sale (
    sale_id int NOT NULL PRIMARY KEY,
    shop_id int NOT NULL,
    loaves smallint NOT NULL CONSTRAINT ck_sale_loaves CHECK (loaves > 0)
);
