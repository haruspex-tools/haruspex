-- One row per part, removed with it. The audit trigger (triggers/stock_audit.sql) returns nothing.
CREATE TABLE depot.stock
(
    part_id int NOT NULL PRIMARY KEY REFERENCES depot.part (part_id) ON DELETE CASCADE,
    qty int NOT NULL CHECK (qty >= 0),
    shelf nvarchar(10) NULL
);
