// Collection-kind detection. Discovery carries return/property types as .NET
// type-name strings; collections are only special-cased as `List<T>` with an
// element-typed `*TypeDefinition` in some places. We detect the common
// enumerable names defensively so arrays emit as `T[]` rather than opaque types.

/** True for .NET enumerable type-name strings (`List<Order>`, `Order[]`, …). */
export function isListReturnType(returnType: string | undefined | null): boolean {
  if (!returnType) return false;
  const t = returnType.trim();
  return (
    /^(List|IList|IReadOnlyList|IReadOnlyCollection|ICollection|IEnumerable|IAsyncEnumerable|Collection|HashSet|ISet)</.test(
      t,
    ) || t.endsWith("[]")
  );
}

/** Element type name of a collection type string: `List<Order>` → `Order`, `Order[]` → `Order`. */
export function elementNameOf(typeStr: string): string {
  const t = typeStr.trim();
  if (t.endsWith("[]")) return t.slice(0, -2).trim();
  const open = t.indexOf("<");
  const close = t.lastIndexOf(">");
  if (open >= 0 && close > open) return t.slice(open + 1, close).trim();
  return t;
}