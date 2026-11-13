-- A member may have been referred by another, which makes a tree for a recursive CTE to walk.
CREATE TABLE lib.member
(
    member_id   int          NOT NULL PRIMARY KEY,
    name        nvarchar(40) NOT NULL,
    email       varchar(80)  NULL,
    referred_by int          NULL REFERENCES lib.member (member_id)
);
