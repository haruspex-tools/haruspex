CREATE TABLE flow.part (
    part_id int NOT NULL PRIMARY KEY,
    name nvarchar(30) NOT NULL,
    unit varchar(4) NULL CONSTRAINT ck_part_unit CHECK (unit IN ('each', 'kg', 'm'))
);
GO

CREATE TABLE flow.component (
    assembly_id int NOT NULL REFERENCES flow.part (part_id),
    part_id int NOT NULL REFERENCES flow.part (part_id),
    quantity smallint NOT NULL CONSTRAINT ck_component_quantity CHECK (quantity > 0),
    PRIMARY KEY (assembly_id, part_id)
);
