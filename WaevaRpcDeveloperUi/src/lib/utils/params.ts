// Shared-Hilfsfunktionen für Parameter-Eingaben in der DevUI.
//
// Die Logik existierte zuvor lokal in EditorPane.svelte, DependencyStep.svelte und
// ParamEditor.svelte (je leicht variierend). Hier konsolidiert als util, genutzt
// vom Batch-Editor. Die bestehenden lokalen Kopien bleiben unangetastet (scope-grenze
// des Batch-Editor-Upgrades) — dieses util ist die Grundlage für künftige gemeinsame
// Nutzung.

import type { ParameterMeta } from 'waeva-rpc-client';

/** Default-Wert für einen einfachen Typnamen (skalar). Komplexe Typen → null. */
export function defaultValueForType(typeName: string): unknown {
  const t = typeName.toLowerCase();
  if (t === 'bool' || t === 'boolean') return false;
  if (
    t === 'int' || t === 'int32' || t === 'int64' || t === 'long' ||
    t === 'number' || t === 'double' || t === 'float' || t === 'decimal' || t === 'single'
  ) return 0;
  if (t === 'string') return '';
  return null;
}

/** Default-Wert für einen Parameter — entfaltet ein TypeDefinition-Objekt zu seinen
 *  Property-Defaults, sonst skalarer Default. Spiegelt EditorPane.defaultValueForParam. */
export function defaultValueForParam(param: ParameterMeta): unknown {
  if (param.typeDefinition?.properties?.length) {
    const obj: Record<string, unknown> = {};
    for (const prop of param.typeDefinition.properties) {
      obj[prop.propertyName] = defaultValueForType(prop.propertyType);
    }
    return obj;
  }
  return defaultValueForType(param.parameterType);
}

/** Wandelt einen Rohtext-Eingabewert in den getippten Skalar um (number/bool), sonst
 *  bleibt der String erhalten. Spiegelt ParamEditor.inferValue. */
export function inferValue(input: string, typeName: string): unknown {
  const t = typeName.toLowerCase();
  if (
    t === 'number' || t === 'int' || t === 'int32' || t === 'int64' || t === 'long' ||
    t === 'double' || t === 'float' || t === 'decimal' || t === 'single'
  ) {
    const n = Number(input);
    return Number.isNaN(n) ? input : n;
  }
  if (t === 'bool' || t === 'boolean') {
    if (input.toLowerCase() === 'true') return true;
    if (input.toLowerCase() === 'false') return false;
  }
  return input;
}