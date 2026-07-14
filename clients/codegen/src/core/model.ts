// EmitterInput — the resolved intermediate the emitters consume. Walking the
// raw DiscoveryInfo once here keeps the language emitters thin and applies the
// wire-correctness fixes (camelCase property names, array unwrap, unknown
// fallback) in a single place.
//
// This is the layer that fixes CodegenPage.svelte's PascalCase-property bug:
// discovery carries PascalCase property names but the wire is camelCase, so
// every emitted property name runs through toCamelCase here.

import type {
  ControllerMeta,
  DiscoveryInfo,
  MethodMeta,
  ParameterMeta,
  PropertyMeta,
  TypeMeta,
} from "trame-client";
import { toCamelCase } from "./casing.js";
import { elementNameOf, isListReturnType } from "./collections.js";
import { NamingResolver } from "./naming.js";
import { csTypeOf, isScalar, isVoidReturn, pyTypeOf, tsTypeOf } from "./scalars.js";

/** A reference to a type — the emitter renders each kind differently. */
export type ResolvedTypeRef =
  | { kind: "scalar"; name: string }            // int, string, bool, …
  | { kind: "array"; element: ResolvedTypeRef } // T[] / List<T>
  | { kind: "ref"; fullName: string }          // a registered ResolvedType
  | { kind: "unknown"; name: string }          // enum / opaque / non-List generic
  | { kind: "any" };                            // object/dynamic/JsonElement

export interface ResolvedProperty {
  /** camelCase wire name (matches the server's CamelCase policy). */
  wireName: string;
  /** Original PascalCase name from discovery (for comments / C# emitter). */
  declaredName: string;
  typeRef: ResolvedTypeRef;
  documentation?: string | null;
}

export interface ResolvedType {
  fullName: string;
  /** Emitted identifier (collision-disambiguated via NamingResolver). */
  emittedName: string;
  properties: ResolvedProperty[];
}

export interface ResolvedParameter {
  /** Parameter name — bound case-sensitively on the wire, kept as-is. */
  name: string;
  typeRef: ResolvedTypeRef;
  documentation?: string | null;
}

export interface ResolvedMethod {
  methodName: string;
  /** camelCase emitted method name (`GetById` → `getById`). */
  emittedName: string;
  controller: string;
  parameters: ResolvedParameter[];
  returnType: ResolvedTypeRef;
  /** void / Task (no result) → the emitter still returns TrameResponse<unknown>. */
  isVoid: boolean;
  documentation?: string | null;
}

export interface ResolvedController {
  name: string;
  /** camelCase accessor name on the root client (`Order` → `order`). */
  accessor: string;
  /** PascalCase emitted class name (`Order` → `OrderClient`). */
  className: string;
  methods: ResolvedMethod[];
}

export interface EmitterInput {
  controllers: ResolvedController[];
  types: ResolvedType[];
  /** Raw discovery, retained for emitters that need the example payloads. */
  discovery: DiscoveryInfo;
}

/** Resolve a .NET type-name string + optional override TypeMeta into a TypeRef. */
export function resolveTypeRef(
  typeStr: string,
  override: TypeMeta | null | undefined,
  resolver: NamingResolver,
  discovery: DiscoveryInfo,
): ResolvedTypeRef {
  const t = (typeStr || "").trim();
  if (!t) return { kind: "unknown", name: "?" };

  // Collection → array of element ref. The override (when present) is the
  // *element* TypeMeta, matching discovery's List<> special-casing.
  if (isListReturnType(t)) {
    const elemName = elementNameOf(t);
    return { kind: "array", element: resolveTypeRef(elemName, override ?? null, resolver, discovery) };
  }

  if (isScalar(t)) return { kind: "scalar", name: t };

  // object/dynamic/JsonElement/Dictionary → any.
  const lower = t.toLowerCase();
  if (
    lower === "object" || lower === "dynamic" || lower === "jsonobject" ||
    lower === "jsonelement" || lower === "jsonnode" || lower === "jsonvalue" ||
    lower === "jsondocument" || lower === "dictionary" || lower === "idictionary" ||
    lower === "expandoobject"
  ) {
    return { kind: "any" };
  }

  // Non-List generic (Dictionary<,>, HashSet, Tuple) → unknown + TODO.
  if (t.includes("<") || t.includes("[")) {
    return { kind: "unknown", name: t };
  }

  // Complex type: is it in the discovery type registry?
  if (lookupTypeMeta(discovery, t)) {
    return { kind: "ref", fullName: t };
  }
  // Otherwise it's an enum or an opaque third-party type → unknown (TODO).
  return { kind: "unknown", name: t };
}

/** Lookup a TypeMeta by friendly name (mirrors shapes.lookupTypeMeta). */
function lookupTypeMeta(discovery: DiscoveryInfo, friendlyName: string): TypeMeta | null {
  const lower = friendlyName.toLowerCase();
  for (const [key, tm] of Object.entries(discovery.types)) {
    const k = key.toLowerCase();
    if (k === lower || k.endsWith("." + lower)) return tm;
  }
  return null;
}

/** Walk DiscoveryInfo once into a ResolvedEmitterInput. */
export function buildEmitterInput(
  discovery: DiscoveryInfo,
  resolver: NamingResolver,
): EmitterInput {
  // Register all types first so collision detection sees the full set.
  resolver.registerByTypeMeta(discovery.types);

  const types: ResolvedType[] = [];
  for (const [fullName, tm] of Object.entries(discovery.types)) {
    types.push({
      fullName,
      emittedName: resolver.resolve(fullName),
      properties: (tm.properties ?? []).map((p) => resolveProperty(p, resolver, discovery)),
    });
  }

  const controllers: ResolvedController[] = (discovery.controllers ?? []).map((c) =>
    resolveController(c, resolver, discovery),
  );

  return { controllers, types, discovery };
}

function resolveProperty(
  prop: PropertyMeta,
  resolver: NamingResolver,
  discovery: DiscoveryInfo,
): ResolvedProperty {
  return {
    wireName: toCamelCase(prop.propertyName),
    declaredName: prop.propertyName,
    typeRef: resolveTypeRef(prop.propertyType, prop.nestedTypeDefinition ?? null, resolver, discovery),
  };
}

function resolveController(
  ctrl: ControllerMeta,
  resolver: NamingResolver,
  discovery: DiscoveryInfo,
): ResolvedController {
  return {
    name: ctrl.name,
    accessor: toCamelCase(ctrl.name),
    className: ctrl.name.charAt(0).toUpperCase() + ctrl.name.slice(1) + "Client",
    methods: (ctrl.methods ?? []).map((m) => resolveMethod(ctrl.name, m, resolver, discovery)),
  };
}

function resolveMethod(
  controllerName: string,
  method: MethodMeta,
  resolver: NamingResolver,
  discovery: DiscoveryInfo,
): ResolvedMethod {
  const isVoid = isVoidReturn(method.returnType);
  return {
    methodName: method.methodName,
    emittedName: toCamelCase(method.methodName),
    controller: controllerName,
    parameters: (method.parameters ?? []).map((p) => resolveParameter(p, resolver, discovery)),
    returnType: isVoid
      ? { kind: "unknown", name: "void" }
      : resolveTypeRef(method.returnType, method.returnTypeDefinition ?? null, resolver, discovery),
    isVoid,
    documentation: method.documentation,
  };
}

function resolveParameter(
  param: ParameterMeta,
  resolver: NamingResolver,
  discovery: DiscoveryInfo,
): ResolvedParameter {
  return {
    name: param.parameterName,
    typeRef: resolveTypeRef(param.parameterType, param.typeDefinition ?? null, resolver, discovery),
    documentation: param.documentation,
  };
}

/** TS type string for a resolved ref (used by the TS + JS emitters). */
export function tsTypeOfRef(ref: ResolvedTypeRef, resolver: NamingResolver): string {
  switch (ref.kind) {
    case "scalar": return tsTypeOf(ref.name);
    case "array": return tsTypeOfRef(ref.element, resolver) + "[]";
    case "ref": return resolver.resolve(ref.fullName);
    case "unknown": return "string"; // enum/opaque → string + TODO at call site
    case "any": return "unknown";
  }
}

/** C# type string for a resolved ref (used by the C# emitter). */
export function csTypeOfRef(ref: ResolvedTypeRef, resolver: NamingResolver): string {
  switch (ref.kind) {
    case "scalar": return csTypeOf(ref.name);
    case "array": return `List<${csTypeOfRef(ref.element, resolver)}>`;
    case "ref": return resolver.resolve(ref.fullName);
    case "unknown": return "string"; // enum/opaque → string + TODO at call site
    case "any": return "object";
  }
}

/** Python type string for a resolved ref (used by the Python emitter). */
export function pyTypeOfRef(ref: ResolvedTypeRef, resolver: NamingResolver): string {
  switch (ref.kind) {
    case "scalar": return pyTypeOf(ref.name);
    case "array": return `list[${pyTypeOfRef(ref.element, resolver)}]`;
    case "ref": return resolver.resolve(ref.fullName);
    case "unknown": return "str"; // enum/opaque → str + TODO at call site
    case "any": return "Any";
  }
}