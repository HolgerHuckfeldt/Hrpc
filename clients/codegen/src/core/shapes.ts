// Shape model — the type-shape primitives lifted verbatim from
// TrameDeveloperUi/src/lib/utils/dependencyCheck.ts (pure TS, no checker).
//
// The *checker* (parsePath/evalPath/compatible/checkSteps) stays in the DevUI
// for now; it imports these primitives. This file is the canonical home for
// the shape model. JSON-kind mapping mirrors System.Text.Json:
//   numbers → number, bool → boolean, string/Guid/DateTime/Uri/char → string,
//   object/dynamic/JsonElement/Dictionary → unknown (acceptsAny), enums → unknown.

import type {
  DiscoveryInfo,
  MethodMeta,
  ParameterMeta,
  PropertyMeta,
  TypeMeta,
} from "trame-client";
import { toCamelCase } from "./casing.js";
import { elementNameOf, isListReturnType } from "./collections.js";
import {
  ANY_NAMES,
  BOOL_NAMES,
  NUMBER_NAMES,
  STRING_NAMES,
} from "./scalars.js";

export type JsonKind = "unknown" | "null" | "boolean" | "number" | "string" | "object" | "array";

export interface TypeShape {
  kind: JsonKind;
  /** For array: element shape. */
  element?: TypeShape;
  /** For object: TypeMeta for the property walk (null when opaque). */
  typeMeta?: TypeMeta | null;
  /** .NET friendly name (for messages). */
  display?: string;
  /** True when the target type accepts arbitrary JSON (object/dynamic/JsonElement/Dictionary). */
  acceptsAny?: boolean;
}

/** Lookup a TypeMeta by friendly name (key ends with `.<name>` or equals it). */
export function lookupTypeMeta(discovery: DiscoveryInfo | null, friendlyName: string): TypeMeta | null {
  if (!discovery) return null;
  const lower = friendlyName.toLowerCase();
  for (const [key, tm] of Object.entries(discovery.types)) {
    const k = key.toLowerCase();
    if (k === lower || k.endsWith("." + lower)) {
      return tm;
    }
  }
  return null;
}

/** Find a property by its camelCase wire name (case-sensitive against the wire document). */
export function findProperty(tm: TypeMeta, name: string): PropertyMeta | undefined {
  // The server serializes camelCase and JsonPath.Net evaluates case-sensitively,
  // so a PascalCase path (`$.Id`) matches nothing at runtime. Compare against
  // toCamelCase(propertyName) to avoid a false-green that fails at runtime.
  return tm.properties.find((p) => toCamelCase(p.propertyName) === name);
}

/** Construct a TypeShape from a .NET type name + optional override TypeMeta. */
export function shapeFromName(
  name: string,
  discovery: DiscoveryInfo | null,
  override?: TypeMeta | null,
): TypeShape {
  const display = name || "?";
  // Explicit TypeMeta (discovery special-cases List<>: this is the *element* TypeMeta).
  if (override && override.properties && override.properties.length > 0) {
    return { kind: "object", typeMeta: override, display };
  }
  const lower = (name || "").toLowerCase().trim();
  if (!name) return { kind: "unknown", display };
  if (ANY_NAMES.has(lower)) return { kind: "unknown", acceptsAny: true, display };
  if (NUMBER_NAMES.has(lower)) return { kind: "number", display };
  if (BOOL_NAMES.has(lower)) return { kind: "boolean", display };
  if (STRING_NAMES.has(lower)) return { kind: "string", display };
  const tm = lookupTypeMeta(discovery, name);
  if (tm) return { kind: "object", typeMeta: tm, display };
  return { kind: "unknown", display };
}

/** Shape of a method's return value (null for void / no method). */
export function returnShape(methodMeta: MethodMeta | null, discovery: DiscoveryInfo | null): TypeShape | null {
  if (!methodMeta) return null;
  const rt = (methodMeta.returnType || "").trim();
  if (!rt || rt.toLowerCase() === "void") return null;
  if (isListReturnType(rt)) {
    const elemName = elementNameOf(rt);
    const element = shapeFromName(elemName, discovery, methodMeta.returnTypeDefinition ?? null);
    return { kind: "array", element, display: rt };
  }
  return shapeFromName(rt, discovery, methodMeta.returnTypeDefinition ?? null);
}

/** Shape of a consumer parameter (target of an @alias). */
export function paramShape(param: ParameterMeta, discovery: DiscoveryInfo | null): TypeShape {
  const pt = (param.parameterType || "").trim();
  if (isListReturnType(pt)) {
    const elemName = elementNameOf(pt);
    const element = shapeFromName(elemName, discovery, param.typeDefinition ?? null);
    return { kind: "array", element, display: pt, acceptsAny: element.acceptsAny };
  }
  return shapeFromName(pt, discovery, param.typeDefinition ?? null);
}

/** Shape of an object property. */
export function propertyShape(prop: PropertyMeta, discovery: DiscoveryInfo | null): TypeShape {
  return shapeFromName(prop.propertyType, discovery, prop.nestedTypeDefinition ?? null);
}