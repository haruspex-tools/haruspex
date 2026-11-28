#!/usr/bin/env bash
# Builds the test DACPACs with Microsoft.Build.Sql and SqlPackage, both of which are DacFx. DacFx's licence allows it in
# development ("to develop and test your applications"), and nothing here ships it: no project in the solution refers to
# it, and this script is run by hand, never by the tests. README.md says what each package is for.
#
# It runs on Linux or macOS, under bash 3.2 (macOS's) or later, so it uses neither bash 4's builtins nor GNU sed's options.
# It needs the .NET SDK (Microsoft.Build.Sql's tasks target .NET 8, so with a later SDK alone set DOTNET_ROLL_FORWARD=Major),
# network access to NuGet, and NuGet's packages in ~/.nuget/packages, where a restore puts them unless told otherwise. shapes
# needs zip; extract needs SqlPackage and a server of your own, as its line below says.
#
# Usage, from the repository root:
#   tests/dacpacs/build.sh fixtures [name...]   every fixture that builds as a project, or the ones named
#   tests/dacpacs/build.sh corpus               WideWorldImporters, from its project's Build items
#   tests/dacpacs/build.sh projects             the projects in projects/, with their references
#   tests/dacpacs/build.sh legacy               the legacy SSDT projects in projects/ (mill), as Visual Studio writes them
#   tests/dacpacs/build.sh ssdt                 WideWorldImporters from its own legacy SSDT project file
#   tests/dacpacs/build.sh versions             the orders fixture under older Microsoft.Build.Sql versions
#   tests/dacpacs/build.sh extract              atelier and orders deployed and extracted again; needs SqlPackage on the
#                                               PATH, HARUSPEX_TEST_SQLSERVER, a server of your own, and SQLCMD (extract())
#   tests/dacpacs/build.sh clr                  the CLR assembly compiled, and the script that deploys it written
#   tests/dacpacs/build.sh shapes               the package in master.dacpac's shape, zipped from its hand-written model.xml
#   tests/dacpacs/build.sh references           winery and cooperage, which refer to other databases and DACPACs, with
#                                               Microsoft.Build.Sql 2.3.0 and 1.0.0 and as a legacy project
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

# A fixture that fails is named at the end, and the step fails, so a fixture whose package isn't built is caught here and not
# only by DacpacEquivalenceTests. checks, adventure-works and wide-world-importers never build, as that test's Unbuilt list
# says with each one's error, so with no names given they are left out; named, they are tried.
fixtures() {
  local names=("$@") failed=()
  if [ ${#names[@]} -eq 0 ]; then
    while IFS= read -r f; do names+=("$f"); done < <(ls "$root/tests/fixtures" | grep -vxE 'README.md|checks|adventure-works|wide-world-importers')
  fi
  for f in "${names[@]}"; do
    local dir="$work/fixtures/$f"
    mkdir -p "$dir"
    cp -R "$root/tests/fixtures/$f/database/." "$dir/"
    project "$f" "$current" Sql170 > "$dir/$f.sqlproj"
    build "$dir" "$f" "$out/fixtures" || failed+=("$f")
  done
  if [ ${#failed[@]} -gt 0 ]; then
    echo "not built: ${failed[*]}"
    return 1
  fi
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
  build "$work/projects/orchard" orchard "$out/orchard"
  build "$work/projects/gauge" gauge "$out/gauge"
  build "$work/projects/turnstile" turnstile "$out/turnstile"
}

# A legacy SSDT project builds with Visual Studio's SSDT on Windows. Here it builds with Microsoft.Build.Sql's own copy of the
# same build targets (SqlTasks), named by SQLDBExtensionsRefPath as the project file's own Import allows, and
# legacy-on-linux.targets, which skips the .NET Framework steps a database project doesn't need. The three other properties
# given only stop .NET Framework's own assemblies being referenced. None of them is a property or item the database build
# reads: the project file is used as it is, and only the build machine differs.
legacy_build() {
  local dir="$1" name="$2" into="$3"; shift 3
  local tasks
  tasks="$(dirname "$(find "$HOME/.nuget/packages/microsoft.build.sql/$current/tools/net8.0" -name Microsoft.Data.Tools.Schema.SqlTasks.targets)")"
  if dotnet build "$dir/$name.sqlproj" -nologo -v:q -clp:ErrorsOnly -p:NetCoreBuild=true -p:SQLDBExtensionsRefPath="$tasks" \
      -p:NETCoreTargetsPath="$tasks" -p:CustomAfterMicrosoftCommonTargets="$out/legacy-on-linux.targets" -p:NoStdLib=true \
      -p:DisableImplicitFrameworkReferences=true -p:AddAdditionalExplicitAssemblyReferences=false "$@" > "$dir/build.log" 2>&1; then
    mkdir -p "$into"
    cp "$dir"/bin/Debug/*.dacpac "$into"/
    echo "built $name into ${into#"$root"/}"
  else
    echo "FAILED $name"
    grep -E " error " "$dir/build.log" | sed -E "s#$dir/##" | sort -u | head -5
    return 1
  fi
}

# The tasks come from Microsoft.Build.Sql's package, which an SDK-style build restores first.
legacy() {
  mkdir -p "$work/projects"
  cp -R "$out/projects/." "$work/projects/"
  build "$work/projects/cellar" cellar "$work/restore" > /dev/null
  legacy_build "$work/projects/mill" mill "$out/mill"
  # legacy-on-linux.targets skips the step that copies a referenced package beside the referring one, so it is copied here.
  cp "$work/projects/granary/bin/Debug/granary.dacpac" "$out/mill/"
}

# WideWorldImporters from its own project file, unchanged, as legacy() builds one. Its DSP names Azure SQL Database, which
# refuses the storage its scripts declare, so it is built for SQL Server 2016 (Sql130): the one property given on the command
# line. The project's CompatibilityMode (130) makes the level the same either way.
ssdt() {
  local from="$root/tests/corpus/sql-server-samples/samples/databases/wide-world-importers/wwi-ssdt/wwi-ssdt"
  local dir="$work/WideWorldImporters-ssdt"
  mkdir -p "$dir" "$work/projects"
  cp -R "$from/." "$dir/"
  cp -R "$out/projects/cellar" "$work/projects/"
  build "$work/projects/cellar" cellar "$work/restore" > /dev/null
  legacy_build "$dir" WideWorldImporters "$work/ssdt" -p:DSP=Microsoft.Data.Tools.Schema.Sql.Sql130DatabaseSchemaProvider
  cp "$work/ssdt/WideWorldImporters.dacpac" "$out/corpus/WideWorldImporters-ssdt.dacpac"
}

# Microsoft.Build.Sql 1.0.0 imports NuGet's pack targets from where SDKs before .NET 10 kept them, so they are linked there for
# its builds.
link_pack_targets() {
  local sdk pack
  sdk="$(dotnet --list-sdks | sed -nE "s#^$(dotnet --version) \[(.*)\]#\1#p")/$(dotnet --version)"
  pack="$sdk/Sdks/NuGet.Build.Tasks.Pack/build"
  if [ ! -e "$pack/NuGet.Build.Tasks.Pack.targets" ]; then
    mkdir -p "$pack"
    ln -sf "$sdk/NuGet.Build.Tasks.Pack.targets" "$pack/"
  fi
}

# Older versions write older model.xml.
versions() {
  link_pack_targets
  for version in 2.0.0 1.0.0; do
    local dir="$work/versions/$version"
    mkdir -p "$dir"
    cp -R "$root/tests/fixtures/orders/database/." "$dir/"
    project "orders" "$version" Sql160 > "$dir/orders.sqlproj"
    build "$dir" orders "$dir/out" && cp "$dir/out/orders.dacpac" "$out/versions/orders-build-sql-$version.dacpac"
  done
}

# A package extracted from a database differs from one built from a project: a module's text starts at line 1, an encrypted
# module's text is replaced, constraints say whether the server trusts them, and an index says whether it is disabled, though a
# primary key or unique constraint whose index is disabled says nothing (decision 0120). So before atelier is extracted, the
# indexes of a nonclustered primary key (pk_shelf), a unique constraint (uq_kiln_serial) and a unique index created apart
# (ix_kiln_serial_peak) are disabled, by $SQLCMD: a sqlcmd command line that reaches the same server. Each package is deployed
# to a database of its own and extracted.
extract() {
  : "${HARUSPEX_TEST_SQLSERVER:?set HARUSPEX_TEST_SQLSERVER to a server of your own}"
  : "${SQLCMD:?set SQLCMD to a sqlcmd command line that reaches the same server, such as docker exec -e SQLCMDPASSWORD <container> /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa}"
  for package in "$out/atelier/atelier.dacpac" "$out/fixtures/orders.dacpac"; do
    local name database
    name="$(basename "$package" .dacpac)"
    database="haruspex_dacpac_$name"
    sqlpackage /a:Publish /sf:"$package" /tcs:"$HARUSPEX_TEST_SQLSERVER;Initial Catalog=$database" > /dev/null
    if [ "$name" = atelier ]; then
      $SQLCMD -d "$database" -b -Q "ALTER INDEX pk_shelf ON studio.shelf DISABLE; ALTER INDEX uq_kiln_serial ON studio.kiln DISABLE; ALTER INDEX ix_kiln_serial_peak ON studio.kiln DISABLE;"
    fi
    sqlpackage /a:Extract /scs:"$HARUSPEX_TEST_SQLSERVER;Initial Catalog=$database" /tf:"$out/versions/$name-extract.dacpac" > /dev/null
    echo "extracted $name; drop the database $database"
  done
}

# A CLR module has no text in a package, only its assembly, class and method. A project's build on Linux refused a .NET
# Framework assembly (SQL70557), so the package is extracted from a database: the assembly in
# projects/kiln-clr is compiled, and a script is written that enables CLR, trusts the assembly by its hash, creates it and the
# modules that use it in a database of their own. Run it with sqlcmd on a server of your own, extract the database, then undo
# what it changed (drop the database, sys.sp_drop_trusted_assembly, and 'clr enabled' back as it was).
clr() {
  local dir="$work/kiln-clr"
  # sha512sum is GNU's; macOS has shasum.
  sha512() { if command -v sha512sum > /dev/null; then sha512sum "$1"; else shasum -a 512 "$1"; fi | cut -d' ' -f1; }
  mkdir -p "$dir"
  cp -R "$out/projects/kiln-clr/." "$dir/"
  dotnet build "$dir/KilnClr.csproj" -c Release -nologo -v:q > "$dir/build.log" 2>&1
  local dll="$dir/bin/Release/net48/KilnClr.dll"
  cat > "$out/kiln-clr-deploy.sql" <<SQL
EXEC sp_configure 'clr enabled', 1; RECONFIGURE;
EXEC sys.sp_add_trusted_assembly 0x$(sha512 "$dll"), N'KilnClr';
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

# The projects that refer to other databases and DACPACs. barrel and tasting are built first into projects/lib, where winery and
# cooperage refer to them as DACPACs a team was given. winery is built with Microsoft.Build.Sql 2.3.0, and again with 1.0.0 from a
# copy in which every project's Sdk names 1.0.0, its DSP SQL Server 2022's (1.0.0's DacFx doesn't know 2025's), and the master
# package's version 160.2.8 (170's fails under it, SQL72025), with barrel and tasting built the same way: ProjectEquivalenceTests
# writes the same copy, its lib from the packages in winery-1.0.0. cooperage is a legacy
# project, built as legacy() builds mill; its reference to SSDT's own master.dacpac is under $(DacPacRootPath), which only Visual
# Studio defines, so the build is given a folder holding the package's master.dacpac at that path. Each build copies master.dacpac
# beside its package, and it is never kept: its licence doesn't allow it (README.md).
references() {
  mkdir -p "$work/projects"
  cp -R "$out/projects/." "$work/projects/"
  build "$work/projects/barrel" barrel "$out/projects/lib"
  build "$work/projects/tasting" tasting "$out/projects/lib"
  cp "$out/projects/lib/"*.dacpac "$work/projects/lib/"
  build "$work/projects/winery" winery "$out/winery"
  rm -f "$out/winery/master.dacpac"

  link_pack_targets
  local old="$work/old"
  mkdir -p "$old"
  cp -R "$work/projects/winery" "$work/projects/vault" "$work/projects/almanac" "$work/projects/barrel" "$work/projects/tasting" "$old/"
  rm -rf "$old"/*/bin "$old"/*/obj
  # Through a copy rather than sed -i, whose argument GNU and BSD sed read differently.
  for file in "$old"/*/*.sqlproj; do
    sed 's#Microsoft.Build.Sql/2.3.0#Microsoft.Build.Sql/1.0.0#; s#Sql170DatabaseSchemaProvider#Sql160DatabaseSchemaProvider#; s#Version="170.0.0"#Version="160.2.8"#' "$file" > "$file.new"
    mv "$file.new" "$file"
  done
  # Its own lib, since 1.0.0 can't load a package 2.3.0 built for SQL Server 2025 (SQL72025); winery-1.0.0 holds the copies.
  build "$old/barrel" barrel "$old/lib" > /dev/null
  build "$old/tasting" tasting "$old/lib" > /dev/null
  build "$old/winery" winery "$out/winery-1.0.0"
  rm -f "$out/winery-1.0.0/master.dacpac"

  local master
  master="$(find "$HOME/.nuget/packages/microsoft.sqlserver.dacpacs.master/170.0.0" -name master.dacpac)"
  mkdir -p "$work/vs/Extensions/Microsoft/SQLDB/Extensions/SqlServer/150/SqlSchemas"
  cp "$master" "$work/vs/Extensions/Microsoft/SQLDB/Extensions/SqlServer/150/SqlSchemas/"
  legacy_build "$work/projects/stave" stave "$work/stave"
  legacy_build "$work/projects/cooperage" cooperage "$out/cooperage" -p:DacPacRootPath="$work/vs"
  # legacy-on-linux.targets skips the step that copies each referenced package beside the referring one, so they are copied here.
  cp "$work/stave/stave.dacpac" "$out/projects/lib/barrel.dacpac" "$out/projects/lib/tasting.dacpac" "$out/cooperage/"
}

mkdir -p "$out/versions"
step="${1:?give fixtures, corpus, projects, legacy, ssdt, versions, extract, clr, shapes or references}"
shift
"$step" "$@"
