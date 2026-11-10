-- A system-versioned table: each change keeps the old row in its history, so a read FOR SYSTEM_TIME ALL returns one row
-- per version of a product.
CREATE TABLE mart.price (
    product_id int NOT NULL CONSTRAINT pk_price PRIMARY KEY CONSTRAINT fk_price_product REFERENCES mart.product (product_id),
    amount decimal(9, 2) NOT NULL,
    valid_from datetime2 GENERATED ALWAYS AS ROW START NOT NULL,
    valid_to datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (valid_from, valid_to)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = mart.price_history));
