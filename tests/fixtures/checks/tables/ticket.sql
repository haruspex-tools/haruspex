-- One column per kind of fact the schema can prove: alias types, value sets, ranges (inclusive and exclusive),
-- minimum lengths, and lengths in each encoding. Some constraints are there to be ignored: one on two columns,
-- one NOT FOR REPLICATION, one a later script disables (ticket_nocheck.sql), and one added later WITH NOCHECK
-- (ticket_late.sql); and one that is disabled and validated again (ticket_restored.sql), which holds. replicated is left out of the
-- procedures: its constraint is enforced on every write but a replication agent's, so the seed can't reach the
-- type's own edges there, and the unit tests show the constraint is not read.
CREATE TABLE chk.ticket
(
    ticket_id   int            NOT NULL CONSTRAINT pk_ticket PRIMARY KEY,
    owner_id    int            NULL CONSTRAINT fk_ticket_owner REFERENCES chk.owner (owner_id),
    code        chk.short_code,
    alt_code    chk.short_code NULL,
    status      varchar(10)    COLLATE Latin1_General_100_CI_AS NOT NULL CONSTRAINT ck_ticket_status CHECK (status IN ('open', 'held', 'closed')),
    priority    tinyint        NULL CONSTRAINT ck_ticket_priority CHECK (priority BETWEEN 1 AND 5),
    score       int            NULL CONSTRAINT ck_ticket_score CHECK (score >= -10 AND score < 100),
    level       smallint       NULL CONSTRAINT ck_ticket_level CHECK (level = 1 OR level = 2 OR level = 4),
    discount    chk.pct    CONSTRAINT ck_ticket_discount CHECK (discount BETWEEN 0 AND 100),
    rate        decimal(9, 4)  NULL,
    big         bigint         NULL CONSTRAINT ck_ticket_big CHECK (big >= 0),
    title       nvarchar(60)   NOT NULL CONSTRAINT ck_ticket_title CHECK (len(title) > 0),
    note        varchar(30)    COLLATE Latin1_General_100_CI_AS NULL CONSTRAINT ck_ticket_note CHECK (note <> ''),
    region      char(2)        COLLATE Latin1_General_100_CI_AS NULL CHECK (region IN ('NE', 'SW')),
    tag_utf8    varchar(12)    COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL,
    pad_utf8    char(4)        COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL,
    muted       int            NULL CONSTRAINT ck_ticket_muted CHECK (muted > 0),
    replicated  int            NULL CONSTRAINT ck_ticket_replicated CHECK NOT FOR REPLICATION (replicated > 0),
    -- Disabled and then validated again by ticket_restored.sql, so trusted.
    restored    int            NULL CONSTRAINT ck_ticket_restored CHECK (restored > 0),
    -- Constrained only by ticket_late.sql, WITH NOCHECK, after a row that breaks it.
    late        int            NULL,
    CONSTRAINT ck_ticket_rate CHECK (rate > 0 AND rate < 1),
    CONSTRAINT ck_ticket_pair CHECK (score <= priority * 20)
);
