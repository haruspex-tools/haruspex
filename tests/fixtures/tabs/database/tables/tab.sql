CREATE TABLE cafe.tab (
    tab_id int NOT NULL CONSTRAINT pk_tab PRIMARY KEY,
    guest_id int NOT NULL CONSTRAINT fk_tab_guest REFERENCES cafe.guest (guest_id),
    opened_at datetime2(0) NOT NULL,
    closed_at datetime2(0) NULL,
    table_no tinyint NULL,
    tip decimal(9,2) NULL,
    note varchar(20) NULL
);
