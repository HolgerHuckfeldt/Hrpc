// Global type aliases for consolidated models.
// The actual types now live in HrpcCommon.Models (HrpcCommon project).
// These aliases ensure existing code using unqualified type names continues to work.

global using HrpcRequest = HrpcCommon.Models.HrpcRequest;
global using HrpcResponse = HrpcCommon.Models.HrpcResponse;
global using HrpcParameter = HrpcCommon.Models.HrpcParameter;
global using HrpcMultiRequest = HrpcCommon.Models.HrpcMultiRequest;
global using ExecutionMode = HrpcCommon.Models.ExecutionMode;
global using HrpcError = HrpcCommon.Models.HrpcError;