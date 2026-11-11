CREATE TABLE sales.[order]
(
    order_id        int               NOT NULL,
    customer_id     int               NOT NULL CONSTRAINT fk_order_customer REFERENCES sales.customer (customer_id),
    salesperson_id  int               NULL CONSTRAINT fk_order_salesperson REFERENCES hr.salesperson (salesperson_id),
    reference       uniqueidentifier  NOT NULL,
    placed_at       datetime2(0)      NOT NULL,
    shipped_at      datetimeoffset(0) NULL,
    delivery_time   time(0)           NULL,
    status          varchar(20)       NOT NULL,
    total           decimal(19, 2)    NOT NULL,
    is_priority     bit               NOT NULL,
    notes           nvarchar(max)     NULL,
    CONSTRAINT pk_order PRIMARY KEY (order_id)
);
