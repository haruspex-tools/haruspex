-- A crate, whose size is a char set and whose tag a unique index keeps apart; a load procedure turns both off.
CREATE TABLE grove.crate (
    crate_id int NOT NULL CONSTRAINT pk_crate PRIMARY KEY,
    size_class char(1) NOT NULL CONSTRAINT ck_crate_size CHECK (size_class IN ('S', 'M', 'L')),
    tag varchar(8) NOT NULL
);
GO
CREATE UNIQUE INDEX ux_crate_tag ON grove.crate (tag);
