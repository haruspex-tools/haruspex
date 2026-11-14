-- Reads what is encrypted: from an extract, a scalar function typed by its declared return type, a table function by the
-- columns the package lists, and a view whose columns are known by name only.
CREATE PROCEDURE studio.calls_secret_fn AS
SELECT studio.secret_double(k.peak) AS doubled FROM studio.kiln AS k;
GO
CREATE PROCEDURE studio.reads_secret_tvf AS
SELECT s.kiln_id, s.label FROM studio.secret_kilns_over(1000) AS s;
GO
CREATE PROCEDURE studio.reads_secret_view AS
SELECT v.kiln_id FROM studio.secret_kilns AS v;
