import { describe, it, expect } from "vitest";
import { HrpcCall } from "../../src/fluent.js";
import { toBase64 } from "../../src/request.js";
import { ExecutionMode } from "../../src/types.js";

describe("HrpcCall", () => {
  it("named + named(id) + exposes -> korrekter HrpcRequest", () => {
    const req = HrpcCall.init("Customer", "Add")
      .with({ name: "Alice" })
      .named("step1")
      .exposes("$.data", "newId")
      .toRequest();
    expect(req.controller).toBe("Customer");
    expect(req.method).toBe("Add");
    expect(req.id).toBe("step1");
    expect(req.dependencyMapping).toEqual({ newId: "$.data" });
    expect(req.binaryData).toBeNull();
    expect(JSON.parse(req.stringData!)).toEqual([
      { parameterName: "name", data: '"Alice"', num: 0 },
    ]);
  });

  it("default id = controller.method", () => {
    expect(HrpcCall.init("C", "M").toRequest().id).toBe("C.M");
  });

  it("positional via array -> param{i} + num", () => {
    const req = HrpcCall.init("C", "M").with([1, 2]).toRequest();
    expect(JSON.parse(req.stringData!)).toEqual([
      { parameterName: "param0", data: "1", num: 0 },
      { parameterName: "param1", data: "2", num: 1 },
    ]);
  });

  it("param() fügt benannten Parameter hinzu", () => {
    const req = HrpcCall.init("C", "M").param("id", 42).toRequest();
    expect(JSON.parse(req.stringData!)).toEqual([
      { parameterName: "id", data: "42", num: 0 },
    ]);
  });

  it("withAlias(@x) fügt Platzhalter-Parameter hinzu", () => {
    const req = HrpcCall.init("C", "M")
      .with(["base"])
      .withAlias("@newId")
      .toRequest();
    const arr = JSON.parse(req.stringData!);
    expect(arr[1]).toEqual({ parameterName: "newId", data: "@newId", num: 1 });
  });

  it("withBinary kodiert als base64", () => {
    const bytes = new Uint8Array([9]);
    const req = HrpcCall.init("C", "M").withBinary(bytes).toRequest();
    expect(req.binaryData).toBe(toBase64(bytes));
  });

  it("leeres exposes -> dependencyMapping null", () => {
    expect(HrpcCall.init("C", "M").toRequest().dependencyMapping).toBeNull();
  });

  it("batch() baut HrpcMultiRequest mit mode", () => {
    const r = HrpcCall.init("C", "M").toRequest();
    const m = HrpcCall.batch([r], ExecutionMode.Serial);
    expect(m.mode).toBe(ExecutionMode.Serial);
    expect(m.requests).toHaveLength(1);
  });
});