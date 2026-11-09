-- A part is bought in (kind 'part', with a cost) or built from other parts (kind 'assembly', no cost of its own).
CREATE TABLE bom.part
(
    part_id   int          NOT NULL PRIMARY KEY,
    name      nvarchar(30) NOT NULL,
    kind      varchar(8)   NOT NULL CONSTRAINT ck_part_kind CHECK (kind IN ('part', 'assembly')),
    unit_cost decimal(9,2) NULL CONSTRAINT ck_part_unit_cost CHECK (unit_cost >= 0)
);
GO
