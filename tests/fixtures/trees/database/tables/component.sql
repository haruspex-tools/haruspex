-- One line of an assembly's bill of materials: how many of a part go into it. A part may be used in many assemblies.
CREATE TABLE bom.component
(
    assembly_id int      NOT NULL REFERENCES bom.part (part_id),
    part_id     int      NOT NULL REFERENCES bom.part (part_id),
    quantity    smallint NOT NULL CONSTRAINT ck_component_quantity CHECK (quantity BETWEEN 1 AND 1000),
    PRIMARY KEY (assembly_id, part_id)
);
GO
