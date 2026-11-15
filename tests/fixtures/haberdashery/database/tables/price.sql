-- A system-versioned price list whose period columns are HIDDEN: SELECT * leaves them out.
CREATE TABLE shop.price (
    sku varchar(12) NOT NULL CONSTRAINT pk_price PRIMARY KEY,
    amount decimal(9, 2) NOT NULL,
    valid_from datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    valid_to datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (valid_from, valid_to)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = shop.price_history));
