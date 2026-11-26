-- Every date and time type, echoed back as the server holds each.
CREATE PROCEDURE till.shift_times
    @day date,
    @opened_at datetime2(0),
    @logged_at datetime = NULL,
    @rota_slot smalldatetime = NULL,
    @till_time time(3) = NULL,
    @stamped_at datetimeoffset(7) = NULL,
    @clocked_at datetime2(7) = NULL
AS
SELECT @day AS day, @opened_at AS opened_at, @logged_at AS logged_at, @rota_slot AS rota_slot, @till_time AS till_time,
    @stamped_at AS stamped_at, @clocked_at AS clocked_at;
