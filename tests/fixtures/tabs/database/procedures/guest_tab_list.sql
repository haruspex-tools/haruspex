-- A guest's tabs through an inline function of many rows; ISNULL gives an open tab a label.
CREATE PROCEDURE cafe.guest_tab_list
    @guest_id int
AS
SET NOCOUNT ON;

SELECT gt.tab_id,
       gt.opened_at,
       gt.closed_at,
       ISNULL(CONVERT(varchar(10), gt.closed_at, 23), 'open') AS state
FROM cafe.guest_tabs(@guest_id) AS gt
ORDER BY gt.tab_id;
