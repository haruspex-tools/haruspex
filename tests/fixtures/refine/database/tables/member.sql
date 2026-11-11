CREATE TABLE lend.member (
    member_id int NOT NULL CONSTRAINT pk_member PRIMARY KEY,
    email varchar(80) NULL CONSTRAINT uq_member_email UNIQUE,
    name nvarchar(60) NOT NULL,
    nickname nvarchar(20) NULL,
    home_branch_id int NULL CONSTRAINT fk_member_branch REFERENCES lend.branch (branch_id)
);
