// The documents a check script is given: each argument a file, or a folder standing for every .json file in it, in ordinal order.
// A test passes the folder it wrote its documents to rather than each file, so the command line stays short however many fixtures
// there are. Windows refuses to start a program whose command line passes 32,767 characters, and EmitCheckPackage.TryRun refuses
// one past 8,191, cmd.exe's limit, on every platform.
import { readdir, stat } from 'node:fs/promises'
import path from 'node:path'

export async function documents(args) {
  const files = []
  for (const arg of args) {
    if ((await stat(arg)).isDirectory()) {
      const names = (await readdir(arg)).filter((name) => name.endsWith('.json'))
      names.sort((a, b) => (a < b ? -1 : a > b ? 1 : 0))
      files.push(...names.map((name) => path.join(arg, name)))
    } else {
      files.push(arg)
    }
  }
  return files
}
