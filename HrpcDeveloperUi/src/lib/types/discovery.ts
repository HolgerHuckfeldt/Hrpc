export interface DiscoveryInfo {
  controllers: ControllerMeta[];
  types: Record<string, TypeMeta>;
}

export interface ControllerMeta {
  name: string;
  methods: MethodMeta[];
}

export interface MethodMeta {
  methodName: string;
  returnType: string;
  returnTypeDefinition?: TypeMeta | null;
  parameters: ParameterMeta[];
  documentation?: string | null;
}

export interface ParameterMeta {
  parameterName: string;
  parameterType: string;
  typeDefinition?: TypeMeta | null;
  documentation?: string | null;
}

export interface TypeMeta {
  typeName: string;
  properties: PropertyMeta[];
  example?: unknown;
}

export interface PropertyMeta {
  propertyName: string;
  propertyType: string;
  nestedTypeDefinition?: TypeMeta | null;
}

export interface HrpcRequest {
  controller: string;
  method: string;
  stringData?: string;
  id?: string;
  dependencyMapping?: Record<string, string> | null;
  binaryData?: number[] | null;
}

export interface HrpcMultiRequest {
  requests: HrpcRequest[];
  mode: ExecutionMode;
}

export enum ExecutionMode {
  Parallel = 0,
  Serial = 1,
}

export interface HrpcResponse {
  code: number;
  data?: string | null;
  content?: number[] | null;
  id?: string | null;
  exposedDependencies?: Record<string, string> | null;
  error?: HrpcError | null;
  isSuccess: boolean;
}

export interface HrpcError {
  code: number;
  message: string;
  details?: string | null;
  requestId?: string | null;
}

export interface HrpcParameter {
  parameterName: string;
  data: string;
}
