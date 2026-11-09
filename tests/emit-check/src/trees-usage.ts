// Type-level checks on the Zod generated from tests/fixtures/trees: recursive types written with Zod 4's getters. tsc
// fails if any of them is wrong, so z.infer must give the recursive type itself, at any depth, without casts.
import {
  ComponentsNodeSchema,
  type CategoryTreeNode,
  type ComponentsNode,
  type GetAssemblyRow,
  type ListItemsRow,
} from '../generated/trees.zod'

// A tree three levels deep: a leaf has no components key (FOR JSON over no rows is NULL, so the key is left out).
export const wheel: ComponentsNode = {
  quantity: 2,
  part: { id: 10, name: 'Wheel assembly', kind: 'assembly' },
  components: [
    { quantity: 1, part: { id: 22, name: 'Hub assembly', kind: 'assembly' }, components: [{ quantity: 2, part: { id: 23, name: 'Bearing', kind: 'part', unitCost: 3.75 } }] },
    { quantity: 4, part: { id: 2147483647, name: 'Bolt M6', kind: 'part', unitCost: 0.05 } },
  ],
}

export const assembly: GetAssemblyRow = { id: 1, name: 'Trailer kit', kind: 'assembly', components: [wheel] }

// The inferred type is the recursive type itself: a grandchild's components are ComponentsNode[] too.
export const grandchildren: ComponentsNode[] | undefined = wheel.components?.[0]?.components?.[0]?.components

// @ts-expect-error A child must be a node, not a part.
export const wrongChild: ComponentsNode = { ...wheel, components: [{ id: 23, name: 'Bearing', kind: 'part' }] }

// @ts-expect-error At any depth.
export const wrongGrandchild: ComponentsNode = { ...wheel, components: [{ ...wheel, components: [{ quantity: '2', part: wheel.part }] }] }

// @ts-expect-error A leaf leaves the key out; it is never null.
export const nullChildren: ComponentsNode = { ...wheel, components: null }

// Mutual recursion: a category's contents hold categories.
export const garden: CategoryTreeNode = {
  id: 5,
  name: 'Garden',
  contents: { items: [{ id: 7, title: 'Hose' }], subcategories: [{ id: 1, name: 'Tools', contents: {} }] },
}

// @ts-expect-error contents is a constructor, never null and never absent.
export const noContents: CategoryTreeNode = { id: 4, name: 'Empty' }

// @ts-expect-error A subcategory is a category, with its contents.
export const wrongSubcategory: CategoryTreeNode = { ...garden, contents: { subcategories: [{ id: 1, name: 'Tools' }] } }

// A plain call is text; json_query over it is the object. A label is never absent when its arguments are never null.
export const item: ListItemsRow = { id: 8, relatedText: '{"id":7,"title":"Hose","category":"Garden"}', related: { id: 7, title: 'Hose', category: 'Garden' }, label: { id: 8, text: 'Gloves' } }

// @ts-expect-error The text is a string, not the object.
export const textAsObject: ListItemsRow = { ...item, relatedText: { id: 7, title: 'Hose', category: 'Garden' } }

// The schema parses a tree at run time too; parse's result has the same type.
export const parsed: ComponentsNode = ComponentsNodeSchema.parse(wheel)
