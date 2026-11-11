-- Every person who can be reached, members and guests alike: a view over UNION ALL, whose key does not survive.
CREATE VIEW rink.contacts
AS
SELECT m.member_id AS person_id, m.name, m.email AS contact, 'member' AS kind
FROM rink.member AS m
UNION ALL
SELECT g.guest_id, g.name, g.phone, 'guest'
FROM rink.guest AS g;
