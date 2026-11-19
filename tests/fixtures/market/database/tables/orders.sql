CREATE TABLE sales.orders (
    order_id int NOT NULL PRIMARY KEY,
    product_id int NOT NULL,
    placed date NOT NULL,
    total decimal(9, 2) NOT NULL,
    status varchar(10) COLLATE Latin1_General_100_CI_AS NOT NULL CHECK (status IN ('open', 'held', 'closed')));
