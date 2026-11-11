CREATE TABLE rink.member (
    member_id int NOT NULL CONSTRAINT pk_member PRIMARY KEY,
    name varchar(40) NOT NULL,
    email varchar(80) NULL,
    level varchar(10) NULL CONSTRAINT ck_member_level CHECK (level IN ('novice', 'skater', 'coach')),
    prefs nvarchar(max) NULL
);
