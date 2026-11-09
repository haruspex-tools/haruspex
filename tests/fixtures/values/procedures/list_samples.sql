-- FOR JSON PATH without INCLUDE_NULL_VALUES: a null value's key is left out.
CREATE PROCEDURE vals.list_samples
    @sample_id int = NULL
AS
SET NOCOUNT ON;

SELECT s.sample_id AS id,
           s.label,
           s.tiny,
           s.small,
           s.whole,
           s.big,
           s.dec_19_2 AS dec19_2,
           s.dec_38_0 AS dec38_0,
           s.num_9_3 AS num9_3,
           s.dec_5_5 AS dec5_5,
           s.cash,
           s.small_cash AS smallCash,
           s.approx,
           s.approx_real AS approxReal,
           s.flag,
           s.day,
           s.t0,
           s.t3,
           s.t7,
           s.dt2_0 AS dt2_0,
           s.dt2_3 AS dt2_3,
           s.dt2_7 AS dt2_7,
           s.dto_0 AS dto0,
           s.dto_3 AS dto3,
           s.dto_7 AS dto7,
           s.small_dt AS smallDt,
           s.legacy_dt AS legacyDt,
           s.guid,
           s.code,
           s.ncode,
           s.vcode,
           s.body
FROM vals.sample AS s
WHERE @sample_id IS NULL OR s.sample_id = @sample_id
ORDER BY s.sample_id
FOR JSON PATH;
