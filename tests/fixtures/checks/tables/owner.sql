CREATE TABLE chk.owner
(
    owner_id  int           NOT NULL CONSTRAINT pk_owner PRIMARY KEY,
    name      nvarchar(20)  NOT NULL CONSTRAINT ck_owner_name CHECK (len(name) >= 2),
    -- Unnamed, so its facts are attributed to where it is declared.
    grade     nchar(1)      NOT NULL CHECK (grade IN (N'A', N'B'))
);
