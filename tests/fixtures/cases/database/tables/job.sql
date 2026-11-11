CREATE TABLE desk.job (
    job_id int NOT NULL CONSTRAINT pk_job PRIMARY KEY,
    customer_id int NOT NULL CONSTRAINT fk_job_customer REFERENCES desk.customer (customer_id),
    status char(1) NOT NULL CONSTRAINT ck_job_status CHECK (status IN ('N', 'W', 'D', 'X')),
    priority tinyint NULL,
    quoted decimal(9,2) NULL,
    promised_on date NULL,
    done_on date NULL
);
