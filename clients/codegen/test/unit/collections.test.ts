import { describe, it, expect } from "vitest";
import { isListReturnType, elementNameOf } from "../../src/core/collections.js";

describe("isListReturnType", () => {
  it("detects List<> and IEnumerable<>", () => {
    expect(isListReturnType("List<Order>")).toBe(true);
    expect(isListReturnType("List<Int32>")).toBe(true);
    expect(isListReturnType("IEnumerable<Order>")).toBe(true);
    expect(isListReturnType("IReadOnlyList<Order>")).toBe(true);
    expect(isListReturnType("HashSet<Order>")).toBe(true);
  });
  it("detects array syntax", () => {
    expect(isListReturnType("Order[]")).toBe(true);
  });
  it("rejects scalars and plain objects", () => {
    expect(isListReturnType("Order")).toBe(false);
    expect(isListReturnType("int")).toBe(false);
    expect(isListReturnType("")).toBe(false);
    expect(isListReturnType(null)).toBe(false);
  });
});

describe("elementNameOf", () => {
  it("extracts the element of a generic collection", () => {
    expect(elementNameOf("List<Order>")).toBe("Order");
    expect(elementNameOf("IEnumerable<Int32>")).toBe("Int32");
  });
  it("extracts the element of an array", () => {
    expect(elementNameOf("Order[]")).toBe("Order");
  });
  it("returns the type as-is when it is not a collection", () => {
    expect(elementNameOf("Order")).toBe("Order");
  });
});