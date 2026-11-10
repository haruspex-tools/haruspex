CREATE TABLE rink.session (
    session_id int NOT NULL CONSTRAINT pk_session PRIMARY KEY,
    starts_at datetime2(0) NOT NULL,
    kind varchar(8) NOT NULL CONSTRAINT ck_session_kind CHECK (kind IN ('public', 'lesson', 'hockey')),
    coach_id int NULL
);
