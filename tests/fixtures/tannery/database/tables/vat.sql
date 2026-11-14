CREATE SCHEMA dye;
GO

-- An invented tannery's vats. Each string set is under a different collation, or none.
CREATE TABLE dye.vat (
    vat_id  int          NOT NULL CONSTRAINT pk_vat PRIMARY KEY,
    -- The database's default collation, declared in haruspex.json: case-insensitive.
    state   varchar(10)  NOT NULL CONSTRAINT ck_vat_state CHECK (state IN ('idle', 'dyeing', 'rinse')),
    -- Padded to four characters; accent-insensitive too.
    shade   char(4)      COLLATE Latin1_General_100_CI_AI NOT NULL CONSTRAINT ck_vat_shade CHECK (shade IN ('teal', 'rose', 'jet')),
    -- Case-sensitive: 'Hot' and 'hot' are two values, so both may be members.
    mode    varchar(8)   COLLATE Latin1_General_100_CS_AS NULL CONSTRAINT ck_vat_mode CHECK (mode IN ('Hot', 'hot', 'cold')),
    -- The soft hyphen is ignored under a _100 collation.
    batch   varchar(12)  COLLATE Latin1_General_100_CI_AS NULL CONSTRAINT ck_vat_batch CHECK (batch IN ('wool-a', 'silk.b', 'flax_c')),
    -- Each of these stays a note: Unicode, a collation not proven, a member that isn't plain, two members that fold alike.
    label   nvarchar(10) COLLATE Latin1_General_100_CI_AS NULL CONSTRAINT ck_vat_label CHECK (label IN (N'warp', N'weft')),
    grade   varchar(8)   COLLATE French_CI_AS NULL CONSTRAINT ck_vat_grade CHECK (grade IN ('fine', 'coarse')),
    tone    varchar(8)   NULL CONSTRAINT ck_vat_tone CHECK (tone IN ('pale', 'deep', 'café')),
    finish  varchar(8)   COLLATE Latin1_General_100_CI_AS NULL CONSTRAINT ck_vat_finish CHECK (finish IN ('matt', 'MATT', 'gloss')));
GO

-- A second table with a set under the same fold, for a set operation over both.
CREATE TABLE dye.rinse_tank (
    tank_id int          NOT NULL CONSTRAINT pk_rinse_tank PRIMARY KEY,
    state   varchar(10)  NOT NULL CONSTRAINT ck_rinse_tank_state CHECK (state IN ('idle', 'drain')));
GO
