import { test } from "node:test";
import assert from "node:assert/strict";
import { previewVersion, builtFrom } from "../version.mjs";

const commit = "250ced9cf25efd9ec2e16ef8097da6b11961ec92";

// semver.org's own pattern for a version, which npm refuses a package.json without.
const semver =
  /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$/;

test("a preview's version names the commit it was built from", () => {
  assert.equal(previewVersion(commit), "0.0.0-preview-250ced9c");
});

test("a commit of digits alone with a leading zero still gives a valid version", () => {
  // As a dot-separated identifier, 01234567 would be a number with a leading zero, which semver forbids.
  const version = previewVersion("0123456789012345678901234567890123456789");
  assert.equal(version, "0.0.0-preview-01234567");
  assert.match(version, semver);
  assert.match(previewVersion(commit), semver);
});

test("only a full commit hash makes a version", () => {
  for (const bad of ["250ced9c", "HEAD", `${commit}\n`, commit.toUpperCase(), ""]) {
    assert.throws(() => previewVersion(bad), /40/, JSON.stringify(bad));
  }
});

test("a binary is built from the commit its version string names", () => {
  const bytes = Buffer.from(`\0\0..10.0.15+8e0f8ed2312954727f8919d7d1c2f78b483b4ee7\0\0.0.1.0-dev+${commit}\0`);
  assert.equal(builtFrom(bytes, commit), true);
  assert.equal(builtFrom(bytes, "8e0f8ed2312954727f8919d7d1c2f78b483b4ee8"), false);
  assert.equal(builtFrom(Buffer.from(commit), commit), false, "the hash alone, without the + of a version, is not enough");
});
