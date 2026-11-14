-- A tree, whose variety is a varchar set under the project's collation.
CREATE TABLE grove.tree (
    tree_id int NOT NULL CONSTRAINT pk_tree PRIMARY KEY,
    variety varchar(12) NOT NULL CONSTRAINT ck_tree_variety CHECK (variety IN ('russet', 'pippin', 'bramley')),
    row_no smallint NULL,
    pressed_id int NULL
);
