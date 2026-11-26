-- A weaving workshop's patterns: each one's draft, the threading and treadling as XML; the weaver's notes, which may be
-- missing or empty; the draft again, validated against its schema; and the pattern's card, one element, named without a schema.
CREATE TABLE loom.pattern (
    pattern_id int NOT NULL CONSTRAINT pk_pattern PRIMARY KEY,
    name nvarchar(40) NOT NULL,
    draft xml NOT NULL,
    notes xml NULL,
    checked_draft xml(loom.draft_schema) NULL,
    card xml(DOCUMENT card_schema) NULL);
