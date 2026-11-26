-- A weaving workshop's patterns: each one's draft, the threading and treadling as XML, and the weaver's notes, which may be
-- missing or empty.
CREATE TABLE loom.pattern (
    pattern_id int NOT NULL CONSTRAINT pk_pattern PRIMARY KEY,
    name nvarchar(40) NOT NULL,
    draft xml NOT NULL,
    notes xml NULL);
