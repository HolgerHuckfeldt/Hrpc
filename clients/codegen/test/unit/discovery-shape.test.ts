import { describe, it, expect } from "vitest";
import { assertDiscoveryShape, DiscoveryShapeError } from "../../src/core/discovery.js";
import { readFixture } from "./fixture.js";

describe("assertDiscoveryShape", () => {
  it("accepts a valid Story-01 payload", () => {
    const d = readFixture();
    expect(d.controllers.length).toBe(6);
    expect(Object.keys(d.types).length).toBe(6);
  });

  it("rejects a non-object payload", () => {
    expect(() => assertDiscoveryShape(null)).toThrow(DiscoveryShapeError);
    expect(() => assertDiscoveryShape("hello")).toThrow(DiscoveryShapeError);
    expect(() => assertDiscoveryShape(42)).toThrow(DiscoveryShapeError);
  });

  it("rejects a payload missing the controllers array", () => {
    expect(() => assertDiscoveryShape({ types: {} })).toThrow(/controllers/);
  });

  it("rejects a payload missing the types object", () => {
    expect(() => assertDiscoveryShape({ controllers: [] })).toThrow(/types/);
  });

  it("rejects a payload where types is an array, not an object", () => {
    expect(() => assertDiscoveryShape({ controllers: [], types: [] })).toThrow(/types/);
  });

  it("rejects a controller entry missing a string name", () => {
    expect(() => assertDiscoveryShape({ controllers: [{}], types: {} })).toThrow(/name/);
  });

  it("rejects a controller missing its methods array", () => {
    expect(() => assertDiscoveryShape({ controllers: [{ name: "X" }], types: {} })).toThrow(/methods/);
  });
});