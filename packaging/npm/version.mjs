// The version of a preview's packages, and the check that a binary was built from the commit that version names.

/**
 * The version every package of a preview carries: 0.0.0, so that any release sorts above it, and the commit's first eight
 * hex digits joined to "preview" by a hyphen. A hyphen rather than a dot keeps it one identifier, never a number: semver
 * compares a numeric identifier as a number and forbids its leading zero, so "preview.01234567" would not be a version.
 */
export function previewVersion(commit) {
  if (!/^[0-9a-f]{40}$/.test(commit)) {
    throw new Error(`expected a commit as 40 lowercase hex digits, not ${JSON.stringify(commit)}`);
  }
  return `0.0.0-preview-${commit.slice(0, 8)}`;
}

/**
 * Whether a binary was built from a commit, and so says the version its packages carry. The build stamps haruspex's
 * informational version into the binary as text: the preview's version, then a `+` and the whole commit
 * (`0.0.0-preview-250ced9c+250ced9c…`, Directory.Build.props). So the binary's `--version` and the packages' version agree,
 * and a binary stamped with any other version, or another commit, is refused. The runtime's and libraries' versions are
 * stamped the same way, each with its own repository's commit, which never matches haruspex's.
 */
export function builtFrom(bytes, commit) {
  return bytes.includes(Buffer.from(`${previewVersion(commit)}+${commit}`, "latin1"));
}
