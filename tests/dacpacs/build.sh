#!/usr/bin/env bash
# Builds the test DACPACs with Microsoft.Build.Sql and SqlPackage, both of which are DacFx. DacFx's licence allows it in
# development ("to develop and test your applications"), and nothing here ships it: no project in the solution refers to
# it, and this script is run by hand, in the dev container, never by the tests. README.md says what each package is for.
#
# Usage, from the repository root, in the dev container (it needs the .NET SDK and network access to NuGet):
#   tests/dacpacs/build.sh fixtures [name...]   every fixture that builds as a project, or the ones named
#   tests/dacpacs/build.sh corpus               WideWorldImporters, from its project's Build items
#   tests/dacpacs/build.sh projects             the projects in projects/, with their references
#   tests/dacpacs/build.sh versions             the orders fixture under older Microsoft.Build.Sql versions
#   tests/dacpacs/build.sh extract              atelier and orders deployed and extracted again; needs SqlPackage on the
#                                               PATH and HARUSPEX_TEST_SQLSERVER, a server of your own
#   tests/dacpacs/build.sh clr                  the CLR assembly compiled, and the script that deploys it written
#   tests/dacpacs/build.sh shapes               the package in master.dacpac's shape, zipped from its hand-written model.xml
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$root/tests/dacpacs"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
current=2.3.0

# An SDK-style project over a folder of scripts: every .sql file under it unless items are given.
project() {
  local name="$1" version="$2" provider="$3" properties="${4:-}" items="${5:-}"
  cat <<XML
<Project Sdk="Microsoft.Build.Sql/$version">
  <PropertyGroup>
    <Name>$name</Name>
    <DSP>Microsoft.Data.Tools.Schema.Sql.${provider}DatabaseSchemaProvider</DSP>
    $properties
  </PropertyGroup>
  <ItemGroup>
$items
  </ItemGroup>
</Project>
XML
}

# Builds the project in $1 (a folder holding $2.sqlproj) and copies every package its bin folder holds into $3.
build() {
  local dir="$1" name="$2" into="$3"
  if dotnet build "$dir/$name.sqlproj" -nologo -v:q -clp:ErrorsOnly > "$dir/build.log" 2>&1; then
    mkdir -p "$into"
    cp "$dir"/bin/Debug/*.dacpac "$into"/
    echo "built $name into ${into#"$root"/}"
  else
    echo "FAILED $name"
    grep -E " error " "$dir/build.log" | sed -E "s#$dir/##" | sort -u | head -5
    return 1
  fi
}

fixtures() {
  local names=("$@")
  [ ${#names[@]} -gt 0 ] || mapfile -t names < <(ls "$root/tests/fixtures" | grep -v README)
  for f in "${names[@]}"; do
    local dir="$work/fixtures/$f"
    mkdir -p "$dir"
    cp -R "$root/tests/fixtures/$f/database/." "$dir/"
    project "$f" "$current" Sql170 > "$dir/$f.sqlproj"
    build "$dir" "$f" "$out/fixtures" || true
  done
}

# WideWorldImporters' own project is the old SSDT format, so an SDK-style one is written with the same Build items, collation
# and compatibility level. Its provider is SQL Server 2016's (Sql130), not the Azure SQL Database one it names: Azure refuses
# the storage its scripts declare.
corpus() {
  local from="$root/tests/corpus/sql-server-samples/samples/databases/wide-world-importers/wwi-ssdt/wwi-ssdt"
  local dir="$work/WideWorldImporters"
  mkdir -p "$dir"
  cp -R "$from/." "$dir/"
  local items
  items="$(grep -oE '<Build Include="[^"]+"' "$from/WideWorldImporters.sqlproj" | sed -E 's#\\#/#g; s#$# />#; s#^#    #')"
  project WideWorldImporters "$current" Sql130 \
    "<DefaultCollation>Latin1_General_100_CI_AS</DefaultCollation><CompatibilityMode>130</CompatibilityMode><EnableDefaultSqlItems>false</EnableDefaultSqlItems>" \
    "$items" > "$dir/WideWorldImporters.sqlproj"
  rm "$dir/WideWorldImporters.sqlproj.user" 2>/dev/null || true
  build "$dir" WideWorldImporters "$out/corpus"
}

# Each project keeps its own .sqlproj. harbour's bin folder holds every package it references (crane, quay through crane,
# tally and master), as a build leaves them, so the test reads them where a user would find them.
projects() {
  mkdir -p "$work/projects"
  cp -R "$out/projects/." "$work/projects/"
  build "$work/projects/atelier" atelier "$out/atelier"
  build "$work/projects/harbour" harbour "$out/harbour"
  build "$work/projects/options-off" options-off "$out/versions"
}

# Older versions write older model.xml. Microsoft.Build.Sql 1.0.0 imports NuGet's pack targets from where SDKs before .NET 10
# kept them, so they are linked there for the build.
versions() {
  local sdk pack
  sdk="$(dotnet --list-sdks | sed -nE "s#^$(dotnet --version) \[(.*)\]#\1#p")/$(dotnet --version)"
  pack="$sdk/Sdks/NuGet.Build.Tasks.Pack/build"
  if [ ! -e "$pack/NuGet.Build.Tasks.Pack.targets" ]; then
    mkdir -p "$pack"
    ln -sf "$sdk/NuGet.Build.Tasks.Pack.targets" "$pack/"
  fi
  for version in 2.0.0 1.0.0; do
    local dir="$work/versions/$version"
    mkdir -p "$dir"
    cp -R "$root/tests/fixtures/orders/database/." "$dir/"
    project "orders" "$version" Sql160 > "$dir/orders.sqlproj"
    build "$dir" orders "$dir/out" && cp "$dir/out/orders.dacpac" "$out/versions/orders-build-sql-$version.dacpac"
  done
}

# A package extracted from a database differs from one built from a project: a module's text starts at line 1, an encrypted
# module's text is replaced, and constraints say whether the server trusts them. Each is deployed to a database of its own,
# extracted, and the database dropped.
extract() {
  : "${HARUSPEX_TEST_SQLSERVER:?set HARUSPEX_TEST_SQLSERVER to a server of your own}"
  for package in "$out/atelier/atelier.dacpac" "$out/fixtures/orders.dacpac"; do
    local name database
    name="$(basename "$package" .dacpac)"
    database="haruspex_dacpac_$name"
    sqlpackage /a:Publish /sf:"$package" /tcs:"$HARUSPEX_TEST_SQLSERVER;Initial Catalog=$database" > /dev/null
    sqlpackage /a:Extract /scs:"$HARUSPEX_TEST_SQLSERVER;Initial Catalog=$database" /tf:"$out/versions/$name-extract.dacpac" > /dev/null
    echo "extracted $name; drop the database $database"
  done
}

# A CLR module has no text in a package, only its assembly, class and method. A project's build in the dev container refuses a
# .NET Framework assembly (SQL70557), so the package is extracted from a database: the assembly in
# projects/kiln-clr is compiled, and a script is written that enables CLR, trusts the assembly by its hash, creates it and the
# modules that use it in a database of their own. Run it with sqlcmd on a server of your own, extract the database, then undo
# what it changed (drop the database, sys.sp_drop_trusted_assembly, and 'clr enabled' back as it was).
clr() {
  local dir="$work/kiln-clr"
  mkdir -p "$dir"
  cp -R "$out/projects/kiln-clr/." "$dir/"
  dotnet build "$dir/KilnClr.csproj" -c Release -nologo -v:q > "$dir/build.log" 2>&1
  local dll="$dir/bin/Release/net48/KilnClr.dll"
  cat > "$out/kiln-clr-deploy.sql" <<SQL
EXEC sp_configure 'clr enabled', 1; RECONFIGURE;
EXEC sys.sp_add_trusted_assembly 0x$(sha512sum "$dll" | cut -d' ' -f1), N'KilnClr';
GO
CREATE DATABASE haruspex_dacpac_clr;
GO
USE haruspex_dacpac_clr;
GO
CREATE SCHEMA studio;
GO
CREATE ASSEMBLY KilnClr FROM 0x$(od -An -v -tx1 "$dll" | tr -d ' \n') WITH PERMISSION_SET = SAFE;
GO
CREATE FUNCTION studio.doubled (@x int) RETURNS int AS EXTERNAL NAME KilnClr.Kiln.[Double];
GO
CREATE PROCEDURE studio.report AS EXTERNAL NAME KilnClr.Kiln.Report;
GO
CREATE PROCEDURE studio.uses_clr AS SELECT studio.doubled(21) AS answer;
SQL
  echo "wrote kiln-clr-deploy.sql: run it, then extract haruspex_dacpac_clr to versions/kiln-clr-extract.dacpac"
}

# master.dacpac can't be committed (README.md), so a package in its shape stands in for it: model.xml written by hand in
# projects/master-shape, zipped as a package is. Needs zip, wherever it runs.
shapes() {
  mkdir -p "$out/master-shape"
  rm -f "$out/master-shape/master.dacpac"
  (cd "$out/projects/master-shape" && zip -q -X "$out/master-shape/master.dacpac" model.xml)
  echo "zipped master-shape/master.dacpac"
}

mkdir -p "$out/versions"
step="${1:?give fixtures, corpus, projects, versions, extract, clr or shapes}"
shift
"$step" "$@"
