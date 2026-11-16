-- A basket's lines, as a table-valued parameter takes them.
CREATE TYPE till.line_list AS TABLE (product_id int NOT NULL, quantity smallint NOT NULL);
