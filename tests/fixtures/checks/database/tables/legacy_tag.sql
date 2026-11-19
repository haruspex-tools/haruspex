-- Created with ANSI_PADDING off, as older scripts often were: its nullable char and binary columns keep their values with
-- their padding cut, so each is at most its length. nchar, and a column that is never null, pad whatever the setting.
SET ANSI_PADDING OFF;

CREATE TABLE chk.legacy_tag
(
    tag_id  int        NOT NULL CONSTRAINT pk_legacy_tag PRIMARY KEY,
    code    char(4)    COLLATE Latin1_General_100_CI_AS NULL,
    kept    char(4)    COLLATE Latin1_General_100_CI_AS NOT NULL,
    wide    nchar(4)   NULL,
    stamp   binary(4)  NULL
);

SET ANSI_PADDING ON;
