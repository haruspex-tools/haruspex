CREATE TABLE dbo.note (note_id int NOT NULL, score tinyint NOT NULL CONSTRAINT ck_note_score CHECK (score BETWEEN 1 AND 10));
