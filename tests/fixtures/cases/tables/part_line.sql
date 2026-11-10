CREATE TABLE desk.part_line (
    line_id int NOT NULL CONSTRAINT pk_part_line PRIMARY KEY,
    job_id int NOT NULL CONSTRAINT fk_part_line_job REFERENCES desk.job (job_id),
    part nvarchar(30) NOT NULL,
    qty smallint NOT NULL,
    price decimal(9,2) NULL
);
