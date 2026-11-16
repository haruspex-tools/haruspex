// A module resolution hook for running generated TypeScript in Node, which strips types but resolves only the files that are
// there: the functions file imports its schemas as './haruspex.zod.js', the file the module compiles to, as TypeScript and
// bundlers map to the '.ts'. Run from source, that file is the '.ts' beside it.
export async function resolve(specifier, context, next) {
  try {
    return await next(specifier, context)
  } catch (error) {
    if (specifier.startsWith('.') && specifier.endsWith('.js')) return next(`${specifier.slice(0, -3)}.ts`, context)
    throw error
  }
}
