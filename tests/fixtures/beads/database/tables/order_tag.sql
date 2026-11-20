CREATE TABLE bead.order_tag
(
    order_id int          NOT NULL REFERENCES bead.[order] (order_id),
    tag      nvarchar(15) NOT NULL,
    PRIMARY KEY (order_id, tag)
);
GO
