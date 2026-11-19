-- Labels with a grade: a unique key, a default (which a row sent never takes) and a closed set of values.
CREATE TYPE till.tag_list AS TABLE (
    tag varchar(20) NOT NULL UNIQUE,
    grade tinyint NOT NULL DEFAULT 1 CHECK (grade IN (1, 2, 3))
);
