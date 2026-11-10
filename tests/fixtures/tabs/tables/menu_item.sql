CREATE TABLE cafe.menu_item (
    item_id int NOT NULL CONSTRAINT pk_menu_item PRIMARY KEY,
    title nvarchar(30) NOT NULL,
    price decimal(9,2) NOT NULL,
    short_code char(4) NULL
);
