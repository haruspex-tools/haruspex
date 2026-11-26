# The test server with full-text search

`FullTextObservationTests` observes `CONTAINSTABLE` and `FREETEXTTABLE`, which need full-text search, and the image the other
differential tests run on (`SqlServerFixture.Image`) doesn't have it. The [Dockerfile](Dockerfile) here adds Microsoft's
`mssql-server-fts` package to that same image, by its digest, so both servers are one build of SQL Server.

The image installs the server unpacked, not as a package, so the full-text package is pinned to the base's version
(`FTS_VERSION`, from the image's `com.microsoft.version` label) and installed without its dependency on `mssql-server`, which
would bring a later server. When `SqlServerFixture.Image` moves, move the `FROM` line and `FTS_VERSION` with it: `apt-cache
madison mssql-server-fts` in the new image, with Microsoft's `mssql-server-2025` repository added, lists the versions.

## Building and running it

From the repository's root, on an x64 machine with Docker:

```sh
docker build -t haruspex-mssql-fts:2025-CU10 tests/Haruspex.DifferentialTests/Server/FullText
docker run -d --name haruspex-mssql-fts -p 14331:1433 -e ACCEPT_EULA=Y -e MSSQL_PID=Developer \
  -e MSSQL_SA_PASSWORD="$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 32)Aa1" haruspex-mssql-fts:2025-CU10
```

Read the password back as for the other server (CONTRIBUTING.md, "Differential tests against SQL Server"), keep it in a file
only you can read, and export the connection string from there before testing:

```sh
export HARUSPEX_TEST_SQLSERVER_FULLTEXT='Server=localhost,14331;User ID=sa;Password=<password>;Encrypt=true;TrustServerCertificate=true'
dotnet test tests/Haruspex.DifferentialTests --filter FullTextObservationTests
```

A test whose process is killed leaves its database behind: drop the databases named `haruspex_test_…` on this server by hand.
