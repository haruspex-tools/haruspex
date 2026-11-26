CREATE PROCEDURE loom.read_card @card nvarchar(max) AS
DECLARE @handle int;
EXEC sp_xml_preparedocument @handle OUTPUT, @card;
SELECT c.weaver, c.loom_width, c.shafts, c.started, c.initials, c.detail, c.note
FROM OPENXML(@handle, '/cards/card', 1)
WITH (
    weaver nvarchar(10),
    loom_width decimal(5, 1) '@width',
    shafts tinyint,
    started date,
    initials char(3) '@weaver',
    detail xml 'detail',
    note varchar(20) 'note') AS c;
EXEC sp_xml_removedocument @handle;
