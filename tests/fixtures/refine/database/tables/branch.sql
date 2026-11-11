CREATE TABLE lend.branch (
    branch_id int NOT NULL CONSTRAINT pk_branch PRIMARY KEY,
    name nvarchar(40) NOT NULL,
    city nvarchar(40) NULL
);
