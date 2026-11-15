CREATE SCHEMA kiosk;
GO

-- An invented chain of market kiosks, deployed once per region: a deployment sets $(Region) and $(Label) to each region's
-- values, and replaces each in the text before SQL Server sees it.
CREATE TABLE kiosk.stall (
    stall_id int          NOT NULL CONSTRAINT pk_stall PRIMARY KEY,
    -- The deployment's own region, or the one every deployment shares: a set only once the variable has its value.
    region   varchar(40)  NOT NULL CONSTRAINT ck_stall_region CHECK (region IN ('$(Region)', 'shared')),
    -- A set no variable touches, which is claimed as usual.
    kind     varchar(10)  NOT NULL CONSTRAINT ck_stall_kind CHECK (kind IN ('food', 'craft'))
);
GO
