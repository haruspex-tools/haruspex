CREATE TABLE gym.member (
    member_id int NOT NULL CONSTRAINT pk_member PRIMARY KEY,
    name varchar(40) NOT NULL,
    nickname nvarchar(20) NULL,
    code char(5) NOT NULL,
    plan_name varchar(10) NOT NULL CONSTRAINT ck_member_plan CHECK (plan_name IN ('basic', 'plus')),
    joined date NOT NULL,
    last_seen datetime NULL,
    born smalldatetime NULL,
    prefs nvarchar(max) NULL
);
