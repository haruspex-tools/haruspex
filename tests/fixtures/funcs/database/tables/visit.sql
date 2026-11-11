CREATE TABLE gym.visit (
    visit_id int NOT NULL CONSTRAINT pk_visit PRIMARY KEY,
    member_id int NOT NULL CONSTRAINT fk_visit_member REFERENCES gym.member (member_id),
    started_at datetime2(3) NOT NULL,
    minutes int NULL,
    room tinyint NOT NULL,
    fee money NOT NULL,
    tip smallmoney NULL,
    rating real NULL,
    weight float NULL
);
