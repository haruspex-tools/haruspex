-- member_id is null for a copy lent to another library.
CREATE TABLE lend.loan (
    loan_id int NOT NULL CONSTRAINT pk_loan PRIMARY KEY,
    branch_id int NOT NULL,
    copy_no int NOT NULL,
    member_id int NULL CONSTRAINT fk_loan_member REFERENCES lend.member (member_id),
    lent_on date NOT NULL,
    due_on date NULL,
    returned_on date NULL,
    fine decimal(6, 2) NULL,
    CONSTRAINT fk_loan_copy FOREIGN KEY (branch_id, copy_no) REFERENCES lend.copy (branch_id, copy_no)
);
