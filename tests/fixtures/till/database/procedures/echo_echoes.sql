-- A key that ignores duplicates, and a type with odd names.
CREATE PROCEDURE till.echo_echoes
    @echoes till.echo_list READONLY,
    @odd till.[odd list] READONLY
AS
SELECT e.k, e.v FROM @echoes AS e ORDER BY e.k;
SELECT o.[first col], o.[select] FROM @odd AS o ORDER BY o.[first col];
