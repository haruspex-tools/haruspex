CREATE TABLE kit.kit_part
(
    part_id  int           NOT NULL PRIMARY KEY,
    kit_id   int           NOT NULL REFERENCES kit.kit (kit_id),
    sku      varchar(8)    NOT NULL,
    qty      tinyint       NOT NULL,
    colour   nvarchar(10)  NULL
);
