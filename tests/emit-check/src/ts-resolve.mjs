// A module resolution hook for running generated TypeScript in Node, which strips types but resolves only the specifiers it
// is given: the functions file imports its schemas as './haruspex.zod', as a bundler resolves it, and Node needs the '.ts'.
export async function resolve(specifier, context, next) {
  try {
    return await next(specifier, context)
  } catch (error) {
    if (specifier.startsWith('.') && !/\.[cm]?[jt]s$/.test(specifier)) return next(`${specifier}.ts`, context)
    throw error
  }
}
