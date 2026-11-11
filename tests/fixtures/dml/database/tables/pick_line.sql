CREATE TABLE depot.pick_line
(
    order_id int NOT NULL,
    part_id int NOT NULL REFERENCES depot.part (part_id),
    qty int NOT NULL CHECK (qty > 0),
    PRIMARY KEY (order_id, part_id)
);
GO
-- Added apart from the table: lines go with their order.
ALTER TABLE depot.pick_line ADD CONSTRAINT fk_pick_line_order FOREIGN KEY (order_id) REFERENCES depot.pick_order (order_id) ON DELETE CASCADE;
