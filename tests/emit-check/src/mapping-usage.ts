// Checks of the mapping helper (generated/haruspex.mapping.ts) against types haruspex generates from the fixtures: a mapper
// that accounts for every key of a row compiles, and one that leaves a key out, at any depth, doesn't. Each case expected to
// fail differs from a case above it that compiles in one respect only, so the error it expects can only be that one.
import type { Exhaustive } from '../generated/haruspex.mapping'
import type { ListCustomersRow } from '../generated/orders.zod'
import type { AuthorStatsRow } from '../generated/payloads.zod'
import type { ComponentsNode, GetAssemblyRow } from '../generated/trees.zod'
import type { GetKitRow } from '../generated/kits.zod'
import type { GetImageRow } from '../generated/gallery.zod'

// A rowset's row: every key used or ignored with a reason. A Date is a value, not an object with keys of its own.
type Customer = Exhaustive<ListCustomersRow, {
  id: 'used'
  name: 'used'
  email: 'used'
  region: 'ignored: the application shows no region'
  createdAt: 'used'
}>

// The row the function returns is what the mapper takes: the account leaves out nothing it uses.
export const customer = (row: ListCustomersRow): Customer => row
export const since = ({ createdAt }: Customer): number => createdAt.getTime()

// @ts-expect-error A key ignored can't be read, so an account can't say it ignores a key the mapper uses.
export const region = (row: Customer): string | null => row.region

// @ts-expect-error A key the row has and the account doesn't, as a column the procedure gains is once the module is regenerated.
export type MissingKey = Exhaustive<ListCustomersRow, { id: 'used'; name: 'used'; email: 'used'; createdAt: 'used' }>

// @ts-expect-error A key the account names and the row doesn't have, as a column the procedure lost is.
export type ExtraKey = Exhaustive<ListCustomersRow, { id: 'used'; name: 'used'; email: 'used'; region: 'used'; createdAt: 'used'; phone: 'used' }>

// @ts-expect-error A reason is required: "ignored: " alone isn't one.
export type NoReason = Exhaustive<ListCustomersRow, { id: 'used'; name: 'used'; email: 'used'; region: 'ignored: '; createdAt: 'used' }>

// @ts-expect-error Each key is "used" or "ignored: " with a reason, and nothing else.
export type Misspelt = Exhaustive<ListCustomersRow, { id: 'usde'; name: 'used'; email: 'used'; region: 'used'; createdAt: 'used' }>

// A row of binary columns: a Buffer, as the driver hands a binary value over, is a value as a Date is, so 'used' accounts
// for it and the mapper reads it as a Buffer.
type Image = Exhaustive<GetImageRow, {
  image_id: 'used'
  thumbnail: 'used'
  checksum: 'used'
  row_version: 'used'
  original: 'ignored: the full image is fetched on its own'
}>

export const image = (row: GetImageRow): Image => row
export const version = ({ row_version }: Image): bigint => row_version.readBigUInt64BE()

// @ts-expect-error A binary column the account leaves out is a missing key like any other.
export type MissingBinaryKey = Exhaustive<GetImageRow, { image_id: 'used'; thumbnail: 'used'; checksum: 'used'; original: 'used' }>

// A tree: an object's keys are accounted for inside it, and a list's through its items, to any depth. The account of a
// recursive type is recursive too.
interface NodeAccount {
  quantity: 'used'
  part: { id: 'used'; name: 'used'; kind: 'ignored: every node of the tree is drawn alike'; unitCost: 'used' }
  components: NodeAccount
}

type Assembly = Exhaustive<GetAssemblyRow, { id: 'used'; name: 'used'; kind: 'used'; components: NodeAccount }>

export const assembly = (row: GetAssemblyRow): Assembly => row
export const cost = (node: Exhaustive<ComponentsNode, NodeAccount>): number =>
  node.quantity * ((node.part.unitCost ?? 0) + (node.components ?? []).reduce((total, child) => total + cost(child), 0))

// @ts-expect-error A key ignored at depth can't be read at depth either.
export const deepKind = (row: Assembly): string | undefined => row.components?.[0]?.components?.[0]?.part.kind

interface NodeMissingCost {
  quantity: 'used'
  part: { id: 'used'; name: 'used'; kind: 'used' }
  components: NodeMissingCost
}

// @ts-expect-error A key left out three levels down, in a part of a component of a component.
export type MissingDeepKey = Exhaustive<GetAssemblyRow, { id: 'used'; name: 'used'; kind: 'used'; components: NodeMissingCost }>

// @ts-expect-error A key that holds objects isn't "used" whole: a key added inside it would pass unseen.
export type UsedWhole = Exhaustive<GetAssemblyRow, { id: 'used'; name: 'used'; kind: 'used'; components: 'used' }>

// A key that holds objects may be ignored whole, with a reason.
export const flat = (row: GetAssemblyRow): Exhaustive<GetAssemblyRow, { id: 'used'; name: 'used'; kind: 'used'; components: 'ignored: the list shows assemblies, not their parts' }> => row

// A union of objects: the account names every key of every variant, here the kit's and the empty marker's.
type Kit = Exhaustive<GetKitRow, {
  kit: { name: 'used'; price: 'used'; parts: { sku: 'used'; qty: 'used'; colour: 'used' } }
  parts: { sku: 'used'; qty: 'used'; colour: 'ignored: picking lists are printed in black' }
  either: { name: 'used'; price: 'used'; parts: { sku: 'used'; qty: 'used'; colour: 'used' }; empty: 'used' }
}>

export const kit = (row: GetKitRow): Kit => row

// @ts-expect-error A key only one variant has is a key all the same.
export type MissingVariantKey = Exhaustive<GetKitRow, {
  kit: { name: 'used'; price: 'used'; parts: { sku: 'used'; qty: 'used'; colour: 'used' } }
  parts: { sku: 'used'; qty: 'used'; colour: 'used' }
  either: { name: 'used'; price: 'used'; parts: { sku: 'used'; qty: 'used'; colour: 'used' } }
}>

// A record's keys are data, not shape: it is accounted for by its values, 'used' for a value with no keys of its own.
type Stats = Exhaustive<AuthorStatsRow, { authorId: 'used'; bookIds: 'used'; prices: 'used' }>

export const stats = (row: AuthorStatsRow): Stats => row
export const dearest = ({ prices }: Stats): number => Math.max(0, ...Object.values(prices))

// And by the values' own account when they are objects, here a record no fixture returns.
interface Shelf { label: string; books: Record<string, { title: string; copies: number }> }

export const shelf = (row: Shelf): Exhaustive<Shelf, { label: 'used'; books: { title: 'used'; copies: 'ignored: stock is counted elsewhere' } }> => row

// @ts-expect-error A record's values are objects, so each of their keys is accounted for.
export type ShelfMissingKey = Exhaustive<Shelf, { label: 'used'; books: { title: 'used' } }>

// @ts-expect-error One decision for a key, not a choice of several: a union would let a reason be left out.
export type SeveralReasons = Exhaustive<ListCustomersRow, { id: 'used'; name: 'used'; email: 'used'; region: 'ignored: ' | 'ignored: x'; createdAt: 'used' }>

// @ts-expect-error Nor used and ignored at once.
export type UsedOrIgnored = Exhaustive<ListCustomersRow, { id: 'used'; name: 'used'; email: 'used'; region: 'used' | 'ignored: maybe'; createdAt: 'used' }>

// @ts-expect-error A reason of spaces alone isn't one.
export type BlankReason = Exhaustive<ListCustomersRow, { id: 'used'; name: 'used'; email: 'used'; region: 'ignored:   '; createdAt: 'used' }>
