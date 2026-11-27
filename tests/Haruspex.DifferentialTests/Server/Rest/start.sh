#!/bin/sh
# Starts the SQL Server RestEndpointObservationTests run on, with 'external rest endpoint enabled', and the HTTPS endpoint it
# calls, on a Docker network of their own (README.md beside this file says why each step is needed). Run from anywhere:
#
#   tests/Haruspex.DifferentialTests/Server/Rest/start.sh [prefix] [port]
#
# The prefix (haruspex-rest by default) names the network, both containers and the folder of certificates; the port (14332 by
# default) is where the server listens on this machine. It prints the connection string to export. Remove everything with
#
#   docker rm -f <prefix>-mssql <prefix>-endpoint && docker network rm <prefix>-net && rm -r <the folder it prints>
set -eu

prefix=${1:-haruspex-rest}
port=${2:-14332}
here=$(cd "$(dirname "$0")" && pwd)
# SqlServerFixture.Image, by the same digest, so these tests see the server the others see.
server_image='mcr.microsoft.com/mssql/server:2025-CU10-ubuntu-24.04@sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3'
node_image='node:24-alpine@sha256:8a32c8cf649a49915a66e81e7a609223c708d0adabf288ee3f4077d765542f85'

# A certificate authority of this run's own, and the endpoint's certificate for the name the tests call, rest.haruspex.test (a
# name under .test, which is never a real host). The server trusts the authority, and nothing else does.
certs=$(mktemp -d "${TMPDIR:-/tmp}/$prefix-certs.XXXXXX")
chmod 755 "$certs"
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$certs/ca.key" -out "$certs/ca.crt" -days 30 -subj '/CN=haruspex test authority' 2>/dev/null
openssl req -newkey rsa:2048 -nodes -keyout "$certs/endpoint.key" -out "$certs/endpoint.csr" -subj '/CN=rest.haruspex.test' 2>/dev/null
printf 'subjectAltName=DNS:rest.haruspex.test\nextendedKeyUsage=serverAuth\n' > "$certs/endpoint.ext"
openssl x509 -req -in "$certs/endpoint.csr" -CA "$certs/ca.crt" -CAkey "$certs/ca.key" -CAcreateserial \
  -out "$certs/endpoint.crt" -days 30 -extfile "$certs/endpoint.ext" 2>/dev/null
mkdir -m 755 "$certs/trusted" && cp "$certs/ca.crt" "$certs/trusted/" && chmod 644 "$certs"/*.key "$certs"/*.crt "$certs/trusted/ca.crt"

docker network create "$prefix-net" >/dev/null
docker run -d --name "$prefix-endpoint" --network "$prefix-net" --network-alias rest.haruspex.test \
  -v "$here:/srv:ro" -v "$certs:/certs:ro" "$node_image" node /srv/endpoint.mjs >/dev/null

# SQL Server on Linux reads the authorities it trusts for outbound TLS from this folder when it starts.
password="$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 32)Aa1"
docker run -d --name "$prefix-mssql" --network "$prefix-net" -p "$port:1433" \
  -e ACCEPT_EULA=Y -e MSSQL_PID=Developer -e MSSQL_MEMORY_LIMIT_MB=2048 -e MSSQL_SA_PASSWORD="$password" \
  -v "$certs/trusted:/var/opt/mssql/security/ca-certificates:ro" "$server_image" >/dev/null

# The option is off by default (31643 on a call); it needs no restart.
for _ in $(seq 60); do
  if docker exec -e SQLCMDPASSWORD="$password" "$prefix-mssql" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b \
    -Q "EXEC sp_configure 'external rest endpoint enabled', 1; RECONFIGURE;" >/dev/null 2>&1; then
    echo "Certificates in $certs. Keep the password in a file only you can read, and export:"
    echo "export HARUSPEX_TEST_SQLSERVER_REST='Server=localhost,$port;User ID=sa;Password=$password;Encrypt=true;TrustServerCertificate=true'"
    exit 0
  fi
  sleep 2
done
echo "The server did not start in two minutes: docker logs $prefix-mssql says why." >&2
exit 1
