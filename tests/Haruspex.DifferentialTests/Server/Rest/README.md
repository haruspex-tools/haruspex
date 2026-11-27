# The test server that calls a REST endpoint

`RestEndpointObservationTests` observes `sys.sp_invoke_external_rest_endpoint`, which needs two things the server the other
differential tests run on (`SqlServerFixture.Image`) doesn't have: the option `external rest endpoint enabled`, off by default,
and an HTTPS endpoint to call whose certificate the server trusts. The procedure refuses plain HTTP (31610).

[`start.sh`](start.sh) starts both, on a Docker network of their own:

- **The endpoint**, [`endpoint.mjs`](endpoint.mjs) in Node's image, by its digest, at `rest.haruspex.test` on the network (a
  name under `.test`, which is never a real host). On port 443 the path picks a reply as a service would send it; on 8443 it
  picks the exact bytes of a reply, for what a service rarely sends, such as a header twice.
- **A certificate authority of the run's own**, made with `openssl`, which signs the endpoint's certificate. SQL Server on Linux
  trusts the authorities it finds in `/var/opt/mssql/security/ca-certificates` when it starts, so the script mounts the
  authority's certificate there, and nothing else trusts it.
- **SQL Server**, the image `SqlServerFixture.Image` names, by the same digest, so both servers are one build, with the option
  turned on once it starts.

## Running it

From the repository's root, on a machine with Docker and `openssl`:

```sh
tests/Haruspex.DifferentialTests/Server/Rest/start.sh haruspex-rest 14332
```

It prints a connection string holding the server's password: keep it in a file only you can read, and export it from there
before testing:

```sh
export HARUSPEX_TEST_SQLSERVER_REST='Server=localhost,14332;User ID=sa;Password=<password>;Encrypt=true;TrustServerCertificate=true'
dotnet test tests/Haruspex.DifferentialTests --filter RestEndpointObservationTests
```

The tests create nothing on the server. Remove both containers, the network and the folder of certificates the script names
when you are done:

```sh
docker rm -f haruspex-rest-mssql haruspex-rest-endpoint && docker network rm haruspex-rest-net
```
