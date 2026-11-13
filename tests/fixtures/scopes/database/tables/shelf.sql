-- In dbo, so that the one-part name shelf reaches it: a CTE named shelf hides it (lib.cte_hides_a_table).
CREATE TABLE dbo.shelf
(
    shelf_id int         NOT NULL PRIMARY KEY,
    label    varchar(10) NOT NULL
);
