CREATE TABLE desk.job_note (
    job_id int NOT NULL CONSTRAINT pk_job_note PRIMARY KEY CONSTRAINT fk_job_note_job REFERENCES desk.job (job_id),
    note nvarchar(100) NULL
);
