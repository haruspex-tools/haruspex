-- Built from its arguments only: a name never null stays never null through the call, and a nickname that may be null
-- may be null.
CREATE FUNCTION cafe.guest_label (@name nvarchar(40), @nickname nvarchar(20))
RETURNS TABLE
AS
RETURN
SELECT COALESCE(@nickname, @name) AS display_name,
       CONCAT(@name, N' (', @nickname, N')') AS full_label,
       @name + N'!' AS greeting,
       @nickname AS nickname;
