#!/usr/bin/env bash
# Checks the repository's prose against docs/style.md: spelling with cspell, the house style with Vale, and that
# every Mermaid diagram parses. CI runs it; run it before committing a change to prose:
#
#   tests/prose/check.sh            all three
#   tests/prose/check.sh spelling   or style, or diagrams: one of them
#
# Each check first runs on the fixtures in tests/prose/fixtures and compares what it reports with what is
# expected there, so a check that has stopped finding anything fails rather than passing in silence. The
# fixtures are then left out of the real run.
#
# Needs Node (for cspell and Mermaid, installed here from the lockfile) and either Vale 3.24.0 on the PATH (or
# its path in VALE) or Docker with a local socket, to run Vale's image. Written for bash 3.2, which macOS has.
set -uo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root" || exit 2
here=tests/prose
fixtures=$here/fixtures
corpus=tests/corpus/sql-server-samples

vale_version=3.24.0
# Pinned by digest, the Docker equivalent of a commit SHA, as in ci.yml.
vale_image=jdkato/vale:v3.24.0@sha256:f5a09410093936d4919d868120786da9789e2652ac321c66a895403eca020ae4

# The files a check reads, one to a line: tracked and new ones, as git lists them (so nothing under bin, obj or
# node_modules), without the fixtures, which are wrong on purpose, and without the vendored corpus, which is Microsoft's,
# kept byte for byte (tests/corpus/README.md), and not ours to judge. Its paths also have spaces, which the word
# splitting below can't take.
files() { git ls-files --cached --others --exclude-standard -- "$@" ":!$fixtures" ":!$corpus"; }

npm_ready() {
  if [ ! -f "$here/node_modules/.package-lock.json" ] || [ "$here/package-lock.json" -nt "$here/node_modules/.package-lock.json" ]; then
    npm ci --prefix "$here" --ignore-scripts --no-audit --no-fund || return 1
  fi
}

run_vale() {
  local binary=${VALE:-vale}
  if command -v "$binary" > /dev/null && "$binary" --version | grep -q "$vale_version"; then
    "$binary" "$@"
  else
    # The image reads the repository through a bind mount, which names a path on the machine Docker runs on. A
    # Docker host over SSH or TCP sees its own files, not these, and Vale would report on nothing.
    local host=${DOCKER_HOST:-$(docker context inspect --format '{{.Endpoints.docker.Host}}' 2> /dev/null)}
    case $host in
      unix://* | npipe://* | "") ;;
      *)
        echo "Vale $vale_version is not on the PATH, and Docker's host is $host, which can't see this folder." >&2
        echo "Install Vale $vale_version (or set VALE to its path), or set DOCKER_CONTEXT to a local Docker." >&2
        return 1
        ;;
    esac
    # Read-only, and with no network: Vale needs neither.
    docker run --rm --network none -v "$root:/repo:ro" -w /repo "$vale_image" "$@"
  fi
}

# Compares what a check reported on the fixtures (one "file:line:what" per line, in $2) with the expected list.
expect() {
  local expected=$1 actual=$2 name=$3
  if ! printf '%s\n' "$actual" | sort -t: -k1,1 -k2,2n -k3 | diff -u "$expected" - >&2; then
    echo "$name: the self-test did not report what $expected expects (diff above)." >&2
    return 1
  fi
  echo "$name: self-test passed."
}

spelling() {
  npm_ready || return 1
  local cspell=$here/node_modules/.bin/cspell found
  # "file:line:col - Unknown word (x)" becomes "file:line:x". cspell exits 1 here: the fixture is misspelt.
  found=$("$cspell" --no-progress --no-summary --no-color "$fixtures/spelling.md" "$fixtures/spelling.cs" \
    | sed -E 's/^([^:]+):([0-9]+):[0-9]+ - [A-Za-z ]+ \(([^)]*)\).*$/\1:\2:\3/')
  expect "$fixtures/spelling.expected" "$found" spelling || return 1
  files '*.md' '*.cs' '.github/workflows/*.yml' | "$cspell" --no-progress --file-list stdin
}

# Vale doesn't read SQL, so the tokens of haruspex.History are applied to .sql files here, with Perl: "file:line:rule" for
# each line that has one. The tokens are read from the rule's YAML, so the two can't drift apart.
history_in_sql() {
  local pattern
  pattern=$(sed -n '/^tokens:/,$ s/^  - //p' .vale/styles/haruspex/History.yml | paste -sd '|' -)
  perl -ne 'BEGIN { $p = shift } print "$ARGV:$.:haruspex.History\n" if /$p/i; close ARGV if eof' "$pattern" "$@"
}

style() {
  local found sql
  # Fails here, saying why, if neither a local Vale nor a local Docker can run it.
  run_vale --version > /dev/null || return 1
  # "file:line:col:rule:message" becomes "file:line:rule". Vale exits 1 here: the fixtures break every rule.
  found=$(run_vale --output=line "$fixtures/style.md" "$fixtures/style.cs" | cut -d: -f1,2,4)
  expect "$fixtures/style.expected" "$found" style || return 1
  found=$(history_in_sql "$fixtures/history.sql")
  expect "$fixtures/history.expected" "$found" "style (SQL)" || return 1
  # shellcheck disable=SC2046
  sql=$(history_in_sql $(files '*.sql'))
  if [ -n "$sql" ]; then
    printf '%s\n' "$sql" >&2
    echo "style: the SQL above names the project's private history (haruspex.History)." >&2
    return 1
  fi
  # One argument per file. The repository's paths have no spaces, which `git ls-files` would otherwise quote.
  # shellcheck disable=SC2046
  run_vale $(files '*.md' '*.cs')
}

diagrams() {
  npm_ready || return 1
  node "$here/diagrams.mjs" --self-test || return 1
  # shellcheck disable=SC2046
  node "$here/diagrams.mjs" $(files '*.md')
}

if [ $# -eq 0 ]; then
  set -- spelling style diagrams
fi
failed=""
for check in "$@"; do
  case $check in
    spelling | style | diagrams) ;;
    *) echo "usage: $0 [spelling] [style] [diagrams]" >&2; exit 2 ;;
  esac
  echo "== $check"
  "$check" || failed="$failed $check"
done

if [ -n "$failed" ]; then
  echo "Failed:$failed. The rules are in docs/style.md." >&2
  exit 1
fi
echo "Prose checks passed: $*."
