CREATE TABLE cafe.tab_item (
    tab_item_id int NOT NULL CONSTRAINT pk_tab_item PRIMARY KEY,
    tab_id int NOT NULL CONSTRAINT fk_tab_item_tab REFERENCES cafe.tab (tab_id),
    item_id int NOT NULL CONSTRAINT fk_tab_item_item REFERENCES cafe.menu_item (item_id),
    qty smallint NOT NULL,
    discount decimal(5,2) NULL
);
