-- A copy is known by its branch and its number there: a key of two columns.
CREATE TABLE lend.copy (
    branch_id int NOT NULL CONSTRAINT fk_copy_branch REFERENCES lend.branch (branch_id),
    copy_no int NOT NULL,
    title_id int NOT NULL CONSTRAINT fk_copy_title REFERENCES lend.title (title_id),
    shelf varchar(8) NULL,
    CONSTRAINT pk_copy PRIMARY KEY (branch_id, copy_no)
);
