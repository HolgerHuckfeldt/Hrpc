<script lang="ts">
  import { onDestroy } from 'svelte';
  import { discoveryState } from '../../state/discovery.svelte.ts';
  import type { ControllerMeta, MethodMeta, ParameterMeta, TypeMeta, PropertyMeta } from 'trame-client';

  let activeLang = $state<'ts' | 'cs'>('ts');
  let copied = $state('');
  let copyTimer = $state<ReturnType<typeof setTimeout> | null>(null);

  onDestroy(() => {
    if (copyTimer) clearTimeout(copyTimer);
  });

  function tsType(t: string): string {
    const map: Record<string, string> = {
      'string': 'string',
      'int': 'number', 'int32': 'number', 'int64': 'number',
      'long': 'number', 'double': 'number', 'float': 'number',
      'decimal': 'number', 'single': 'number', 'number': 'number',
      'bool': 'boolean', 'boolean': 'boolean',
      'datetime': 'string',
    };
    if (map[t.toLowerCase()]) return map[t.toLowerCase()];
    // Could be a complex type name — use the short name
    const short = t.includes('.') ? t.split('.').pop()! : t;
    return short;
  }

  function csType(t: string): string {
    const map: Record<string, string> = {
      'string': 'string',
      'int': 'int', 'int32': 'int', 'int64': 'long',
      'long': 'long', 'double': 'double', 'float': 'float',
      'decimal': 'decimal', 'single': 'float', 'number': 'double',
      'bool': 'bool', 'boolean': 'bool',
      'datetime': 'DateTime',
    };
    if (map[t.toLowerCase()]) return map[t.toLowerCase()];
    const short = t.includes('.') ? t.split('.').pop()! : t;
    return short;
  }

  function shortName(fullName: string): string {
    return fullName.includes('.') ? fullName.split('.').pop()! : fullName;
  }

  // ---- TypeScript Interfaces (with nested types) ----
  function generateTsInterfaces(): string {
    const types = discoveryState.data?.types;
    if (!types || Object.keys(types).length === 0) return '';

    const generated = new Set<string>();
    const interfaces: string[] = [];

    function buildInterface(fullName: string, typeMeta: TypeMeta) {
      const name = shortName(fullName);
      if (generated.has(name)) return;
      generated.add(name);

      const props: string[] = [];
      for (const prop of typeMeta.properties) {
        const pt = tsType(prop.propertyType);
        props.push(`  ${prop.propertyName}: ${pt};`);

        // Recurse into nested type definitions
        if (prop.nestedTypeDefinition && prop.nestedTypeDefinition.properties?.length) {
          const nestedName = shortName(prop.nestedTypeDefinition.typeName || prop.propertyType);
          if (!generated.has(nestedName)) {
            buildInterface(prop.nestedTypeDefinition.typeName || prop.propertyType, prop.nestedTypeDefinition);
          }
        }
      }
      interfaces.push(`interface ${name} {\n${props.join('\n')}\n}`);
    }

    for (const [fullName, typeMeta] of Object.entries(types)) {
      buildInterface(fullName, typeMeta);
    }
    return '\n// ---- Data Types ----\n\n' + interfaces.join('\n\n') + '\n';
  }

  // ---- C# Classes (with nested types) ----
  function generateCsClasses(): string {
    const types = discoveryState.data?.types;
    if (!types || Object.keys(types).length === 0) return '';

    const generated = new Set<string>();
    const classes: string[] = [];

    function buildClass(fullName: string, typeMeta: TypeMeta) {
      const name = shortName(fullName);
      if (generated.has(name)) return;
      generated.add(name);

      const props: string[] = [];
      for (const prop of typeMeta.properties) {
        const pt = csType(prop.propertyType);
        props.push(`    public ${pt} ${prop.propertyName} { get; set; }`);

        if (prop.nestedTypeDefinition && prop.nestedTypeDefinition.properties?.length) {
          const nestedName = shortName(prop.nestedTypeDefinition.typeName || prop.propertyType);
          if (!generated.has(nestedName)) {
            buildClass(prop.nestedTypeDefinition.typeName || prop.propertyType, prop.nestedTypeDefinition);
          }
        }
      }
      classes.push(`public class ${name}\n{\n${props.join('\n')}\n}`);
    }

    for (const [fullName, typeMeta] of Object.entries(types)) {
      buildClass(fullName, typeMeta);
    }
    return '\n// ---- Data Types ----\n\n' + classes.join('\n\n') + '\n';
  }

  function paramsToTs(params: ParameterMeta[]): string {
    return params.map(p => `${p.parameterName}: ${tsType(p.parameterType)}`).join(', ');
  }

  function paramsToCs(params: ParameterMeta[]): string {
    return params.map(p => `${csType(p.parameterType)} ${p.parameterName}`).join(', ');
  }

  function buildArgsTs(params: ParameterMeta[]): string {
    if (params.length === 0) return '[]';
    return '[\n' + params.map(p => `    { parameterName: "${p.parameterName}", data: JSON.stringify(${p.parameterName}) }`).join(',\n') + '\n  ]';
  }

  function buildArgsCs(params: ParameterMeta[]): string {
    if (params.length === 0) return 'new TrameCallArg[0]';
    return 'new TrameCallArg[] {\n' + params.map(p => `        new("${p.parameterName}", JsonSerializer.Serialize(${p.parameterName}))`).join(',\n') + '\n    }';
  }

  function methodTsName(ctrl: string, method: string): string {
    return `${ctrl.charAt(0).toLowerCase() + ctrl.slice(1)}_${method}`;
  }

  function methodCsName(ctrl: string, method: string): string {
    return `${ctrl}_${method}`;
  }

  function generateTs(): string {
    const controllers = discoveryState.data?.controllers ?? [];
    if (controllers.length === 0) return '// No controllers discovered.';

    const methods: string[] = [];
    for (const ctrl of controllers) {
      for (const method of ctrl.methods) {
        const p = paramsToTs(method.parameters);
        const args = buildArgsTs(method.parameters);
        const returnType = tsType(method.returnType);
        const fnName = methodTsName(ctrl.name, method.methodName);
        methods.push(
`  /** ${ctrl.name}.${method.methodName} */
  async ${fnName}(${p}): Promise<TrameResponse<${returnType}>> {
    return this.call("${ctrl.name}", "${method.methodName}", ${args});
  }`
        );
      }
    }

    const typeInterfaces = generateTsInterfaces();

    return `// Auto-generated Trame TypeScript Client
// Base URL: /api/trame
${typeInterfaces}
interface TrameParameter {
  parameterName: string;
  data: string;
}

interface TrameResponse<T = unknown> {
  code: number;
  data: T | null;
  id: string | null;
  error: { code: number; message: string } | null;
  isSuccess: boolean;
}

class TrameClient {
  constructor(private baseUrl: string = '/api/trame') {}

  private async call<T>(controller: string, method: string, params: TrameParameter[]): Promise<TrameResponse<T>> {
    const res = await fetch(\`\${this.baseUrl}/json\`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ controller, method, stringData: JSON.stringify(params), id: \`\${controller}.\${method}\` }),
    });
    return res.json();
  }

${methods.join('\n\n')}
}

// Usage:
// const client = new TrameClient('/api/trame');
// const res = await client.getById(42);
// console.log(res.data);

export { TrameClient, type TrameResponse, type TrameParameter };
`;
  }

  function generateCs(): string {
    const controllers = discoveryState.data?.controllers ?? [];
    if (controllers.length === 0) return '// No controllers discovered.';

    const methods: string[] = [];
    for (const ctrl of controllers) {
      for (const method of ctrl.methods) {
        const p = paramsToCs(method.parameters);
        const args = buildArgsCs(method.parameters);
        const returnType = csType(method.returnType);
        const fnName = methodCsName(ctrl.name, method.methodName);
        methods.push(
`    /// <summary>${ctrl.name}.${method.methodName}</summary>
    public async Task<TrameResponse<${returnType}>> ${fnName}Async(${p}) =>
        await CallAsync<${returnType}>("${ctrl.name}", "${method.methodName}", ${args});`
        );
      }
    }

    const typeClasses = generateCsClasses();

    return `// Auto-generated Trame C# Client
// Install-Package Trame.Client

using TrameClient.Trame;
using System.Text.Json;
${typeClasses}
public class TrameClient
{
    private readonly ITrameClient _client;

    public TrameClient(string baseUrl)
    {
        _client = new TrameRestJsonClient(baseUrl);
    }

    public TrameClient(ITrameClient client)
    {
        _client = client;
    }

    private async Task<TrameResponse<T>> CallAsync<T>(string controller, string method, TrameCallArg[] args)
    {
        var request = TrameCall.Init(controller, method);
        foreach (var arg in args)
            request.Add(arg.ParameterName, arg.Data);
        return await _client.Call<T>(request.ToRequest());
    }

${methods.join('\n\n')}
}

public record TrameCallArg(string ParameterName, string Data);

// Usage:
// var client = new TrameClient("https://localhost:5001/api/trame");
// var result = await client.GetByIdAsync(42);
// Console.WriteLine(result.Data);
`;
  }

  let code = $derived(activeLang === 'ts' ? generateTs() : generateCs());

  async function copy() {
    try {
      await navigator.clipboard.writeText(code);
      copied = activeLang;
      if (copyTimer) clearTimeout(copyTimer);
      copyTimer = setTimeout(() => { copied = ''; copyTimer = null; }, 1500);
    } catch { /* ignore */ }
  }
</script>

<div class="codegen-page">
  <div class="codegen-header">
    <div class="lang-tabs">
      <button
        class="ghost small"
        class:active={activeLang === 'ts'}
        onclick={() => (activeLang = 'ts')}
      >TypeScript</button>
      <button
        class="ghost small"
        class:active={activeLang === 'cs'}
        onclick={() => (activeLang = 'cs')}
      >C#</button>
    </div>
    <button class="primary small" onclick={copy}>
      {copied === activeLang ? 'Copied!' : `Copy ${activeLang === 'ts' ? 'TypeScript' : 'C#'}`}
    </button>
  </div>
  <pre class="code codegen-output"><code>{code}</code></pre>
</div>

<style>
  .codegen-page {
    display: flex;
    flex-direction: column;
    height: 100%;
    min-height: 0;
  }
  .codegen-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 8px;
    flex-shrink: 0;
  }
  .lang-tabs {
    display: flex;
    gap: 2px;
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    overflow: hidden;
  }
  .lang-tabs button {
    border: none;
    border-radius: 0;
    padding: 4px 12px;
  }
  .lang-tabs button.active {
    background: var(--accent-secondary);
    color: #fff;
  }
  .codegen-output {
    flex: 1;
    min-height: 0;
    overflow: auto;
    padding: 12px;
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    background: var(--code-bg);
    color: var(--code-text);
    font-family: var(--font-mono);
    font-size: 0.82rem;
    line-height: 1.6;
    white-space: pre;
    margin: 0;
  }
  .codegen-output code {
    font-family: var(--font-mono);
  }
</style>
