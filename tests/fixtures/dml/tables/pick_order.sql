CREATE TABLE depot.pick_order
(
    order_id int IDENTITY(1, 1) NOT NULL PRIMARY KEY,
    placed_at datetime2(0) NOT NULL,
    status varchar(10) NOT NULL CHECK (status IN ('open', 'picked', 'cancelled')),
    note nvarchar(200) NULL
);
