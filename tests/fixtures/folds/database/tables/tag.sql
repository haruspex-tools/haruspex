CREATE TABLE store.tag (
    tag_id int NOT NULL CONSTRAINT pk_tag PRIMARY KEY,
    product_id int NOT NULL,
    name nvarchar(20) NOT NULL
);
