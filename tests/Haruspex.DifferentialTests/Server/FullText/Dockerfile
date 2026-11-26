# The differential tests' SQL Server with full-text search, for the tests that need it (README.md beside this file says how
# to build and run it). The base is SqlServerFixture.Image, by the same digest, so these tests see the server the others see.
FROM mcr.microsoft.com/mssql/server:2025-CU10-ubuntu-24.04@sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3

# The full-text package built for the base's server, 17.0.5035.5 (the image's com.microsoft.version label): a full-text
# package must be the version of the server it extends. Move it with the base.
ARG FTS_VERSION=17.0.5035.5-3

USER root

# The image has the server unpacked rather than installed as a package, so the package's dependency on mssql-server is
# ignored: apt would otherwise bring a second, later server. The base already trusts Microsoft's signing key.
RUN set -eux; \
    echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/ubuntu/24.04/mssql-server-2025 noble main" \
        > /etc/apt/sources.list.d/mssql-server-2025.list; \
    apt-get update; \
    cd /tmp; \
    apt-get download "mssql-server-fts=${FTS_VERSION}"; \
    dpkg -i --ignore-depends=mssql-server mssql-server-fts_*.deb; \
    rm mssql-server-fts_*.deb /etc/apt/sources.list.d/mssql-server-2025.list; \
    rm -rf /var/lib/apt/lists/*

USER mssql
