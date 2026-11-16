// Stands in for Next.js's server-only package, which a module generated with "serverOnly": true imports for its side effect:
// a build fails where browser code imports the module. The package has no exports, so an empty module stands in for it when
// the generated code is compiled, without adding a dependency only a Next.js application needs.
export {}
