// Global type aliases for consolidated models.
// The actual types now live in WaevaRpcCommon.Models (WaevaRpcCommon project).
// These aliases ensure existing code using unqualified type names continues to work.

global using WaevaRpcRequest = WaevaRpcCommon.Models.WaevaRpcRequest;
global using WaevaRpcResponse = WaevaRpcCommon.Models.WaevaRpcResponse;
global using WaevaRpcParameter = WaevaRpcCommon.Models.WaevaRpcParameter;
global using WaevaRpcMultiRequest = WaevaRpcCommon.Models.WaevaRpcMultiRequest;
global using ExecutionMode = WaevaRpcCommon.Models.ExecutionMode;
global using WaevaRpcError = WaevaRpcCommon.Models.WaevaRpcError;