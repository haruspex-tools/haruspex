# Infer from the source, never from a live database

**Date:** 7 Nov 2026

**Decision.** The tool reads a database project's source (a folder of `.sql` files today) and infers every result's
shape from it. It never connects to a database: no connection string, no credentials, no network.

**Context.** An application that calls stored procedures needs the shape of what each returns. SQL Server can describe a
result itself, and a tool could also run each procedure and record what comes back.

**Alternatives.**
- *Ask the server* (`sp_describe_first_result_set`). It describes a `FOR JSON` result as one `nvarchar(max)` column, says a
  column allows NULL whenever it cannot tell, and describes only the first result set. It also needs the database deployed
  and a login to it.
- *Run each procedure and record the output.* What comes back shows only what the data at hand happened to produce: an
  empty table proves nothing, and no number of runs proves that a value is never null. It needs data, credentials and a way
  to call every procedure safely, including those that write.
- *Write the types by hand.* Nothing checks them, and they drift from the SQL.

**Reason.** Everything that decides a result's shape is in the source: the query, the schema it reads, and the rules SQL
Server applies to both. Reading it gives claims that hold for every execution, not only for those seen. Reading schema and
never data is also easy for a security or governance owner to approve, and works in CI with nothing but the repository.

**Amended** (15 Nov 2026, decisions [0040](0040-a-dacpac-is-read-with-the-packages-of-its-database.md) and
[0044](0044-a-project-file-is-evaluated-in-part-and-the-rest-reported.md)). "A folder of `.sql` files today" was the one
source then. The source may also be a DACPAC, read with the packages of its database, or a database project's `.sqlproj`,
evaluated in part (the part of MSBuild a database project's build reads, the rest reported), and since [decision 0127](0127-a-source-may-be-several-folders.md) several folders read as
one database. Each is a file or a folder of the project, never a server.

**See also:** [the guide, "The shape of the problem"](../internals/01-the-shape-of-the-problem.md).
