-- GROUP BY a column named by its table's schema and name, and a star over another table so named, outer-joined.
CREATE PROCEDURE lib.full_name_groups
AS
SELECT lib.loan.member_id, COUNT_BIG(*) AS loans
FROM lib.loan
GROUP BY lib.loan.member_id
ORDER BY lib.loan.member_id;

SELECT lib.member.*, b.title
FROM lib.member
LEFT JOIN lib.book AS b ON b.donor_id = lib.member.member_id
ORDER BY lib.member.member_id, b.title;
