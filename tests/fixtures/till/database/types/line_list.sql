-- A basket's lines, as a table-valued parameter takes them: a key, a CHECK and a nullable column.
CREATE TYPE till.line_list AS TABLE (
    product_id int NOT NULL,
    quantity smallint NOT NULL CHECK (quantity > 0),
    note nvarchar(200) NULL,
    PRIMARY KEY (product_id)
);
