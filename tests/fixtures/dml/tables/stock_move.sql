CREATE TABLE depot.stock_move
(
    move_id int IDENTITY(1, 1) NOT NULL PRIMARY KEY,
    part_id int NOT NULL,
    delta int NOT NULL,
    moved_at datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
