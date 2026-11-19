-- Every column of a table-valued parameter, as sent.
CREATE PROCEDURE till.echo_readings
    @readings till.reading_list READONLY
AS
SELECT * FROM @readings ORDER BY taken_on;
