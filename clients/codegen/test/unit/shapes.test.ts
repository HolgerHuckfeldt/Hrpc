import { describe, it, expect } from "vitest";
import type { DiscoveryInfo } from "trame-client";
import {
  shapeFromName, returnShape, paramShape, lookupTypeMeta, findProperty,
} from "../../src/core/shapes.js";
import { readFixture } from "./fixture.js";

const discovery = readFixture();

describe("shapeFromName", () => {
  it("maps scalars to their JSON kind", () => {
    expect(shapeFromName("int", null).kind).toBe("number");
    expect(shapeFromName("bool", null).kind).toBe("boolean");
    expect(shapeFromName("string", null).kind).toBe("string");
    expect(shapeFromName("DateTime", null).kind).toBe("string");
  });
  it("maps any-shaped kinds to unknown + acceptsAny", () => {
    const s = shapeFromName("object", null);
    expect(s.kind).toBe("unknown");
    expect(s.acceptsAny).toBe(true);
  });
  it("resolves a registered type to object", () => {
    const s = shapeFromName("Order", discovery);
    expect(s.kind).toBe("object");
    expect(s.typeMeta).toBeTruthy();
  });
  it("falls back to unknown for an unregistered name (enum/opaque)", () => {
    expect(shapeFromName("NotAType", null).kind).toBe("unknown");
  });
});

describe("returnShape", () => {
  it("unwraps List<T> to an array of element shape", () => {
    const m = discovery.controllers.flatMap((c) => c.methods).find((m) => m.methodName === "GetByArticles")!;
    const s = returnShape(m, discovery);
    expect(s?.kind).toBe("array");
    expect(s?.element?.kind).toBe("object");
  });
  it("returns null for void", () => {
    const m = { methodName: "Do", returnType: "void", parameters: [] } as never;
    expect(returnShape(m as never, discovery)).toBeNull();
  });
});

describe("paramShape", () => {
  it("unwraps List<int> to an array of number", () => {
    const m = discovery.controllers.flatMap((c) => c.methods).find((m) => m.methodName === "GetByArticles")!;
    const p = m.parameters.find((p) => p.parameterName === "articleIds")!;
    const s = paramShape(p, discovery);
    expect(s.kind).toBe("array");
    expect(s.element?.kind).toBe("number");
  });
});

describe("lookupTypeMeta + findProperty (camelCase wire match)", () => {
  it("finds a registered type by friendly name", () => {
    const tm = lookupTypeMeta(discovery, "Order");
    expect(tm?.properties.length).toBeGreaterThan(0);
  });
  it("finds a property by its camelCase wire name, not PascalCase", () => {
    const tm = lookupTypeMeta(discovery, "Order")!;
    expect(findProperty(tm, "customerId")).toBeDefined();   // camelCase wire name → found
    expect(findProperty(tm, "CustomerId")).toBeUndefined(); // PascalCase → not on the wire
  });
});