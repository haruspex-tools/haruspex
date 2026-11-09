-- Children of a sample, so a nested FOR JSON subquery can be empty for some samples.
CREATE TABLE vals.sample_tag
(
    sample_tag_id  int           NOT NULL CONSTRAINT pk_sample_tag PRIMARY KEY,
    sample_id      int           NOT NULL CONSTRAINT fk_sample_tag_sample REFERENCES vals.sample (sample_id),
    name           nvarchar(20)  NOT NULL,
    note           nvarchar(40)  NULL
);
