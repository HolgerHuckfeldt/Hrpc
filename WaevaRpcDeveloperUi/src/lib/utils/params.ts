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

/** Wandelt einen PascalCase-Bezeichner in camelCase um. Spiegelt
 *  System.Text.Json.JsonNamingPolicy.CamelCase: führende Großbuchstaben werden
 *  kleingeschrieben, außer der letzte einer längeren Folge, auf den ein
 *  Kleinbuchstabe folgt (Id→id, ID→id, IPAddress→ipAddress).
 *
 *  Warum: Die Discovery liefert Property-Namen per Reflection in PascalCase (Id,
 *  Name …), der Server serialisiert das Ergebnis aber camelCase (id, name …).
 *  JsonPath.Net (DependencyResolver.ExtractValue) ist case-sensitiv — ein
 *  JsonPath `$[0].Id` findet in `{"id":1,…}` nichts. Die DevUI muss also
 *  camelCase-Vorschläge anbieten, damit Exposes nicht ins Leere laufen. */
export function toCamelCase(name: string): string {
  if (!name) return name;
  const chars = [...name];
  let i = 0;
  while (i < chars.length && chars[i] >= 'A' && chars[i] <= 'Z') i++;
  if (i === 0) return name; // beginnt klein → unverändert (ist schon camelCase)
  let end = i;
  if (i > 1 && i < chars.length && chars[i] >= 'a' && chars[i] <= 'z') end = i - 1;
  for (let k = 0; k < end; k++) chars[k] = chars[k].toLowerCase();
  return chars.join('');
}

/** Erkennt am ReturnType-String (z. B. "List<AdresseX>"), ob die Methode eine
 *  Liste/ein Array zurückgibt. Die Discovery special-cast nur List<> mit einem
 *  element-typisierten ReturnTypeDefinition; wir erkennen gängige Enumerable-
 *  Namen defensiv, damit für Listen `$[0].prop` statt `$.prop` vorgeschlagen wird. */
export function isListReturnType(returnType: string | undefined | null): boolean {
  if (!returnType) return false;
  const t = returnType.trim();
  return (
    /^(List|IList|IReadOnlyList|IReadOnlyCollection|ICollection|IEnumerable|Collection|HashSet|ISet)</.test(t) ||
    t.endsWith('[]')
  );
}

/** JsonPath-Vorschläge für Exposes: `$` + bei Listen `$[0]` + pro Property der
 *  Element-/Objekt-Typen, jeweils camelCase und mit dem passenden Präfix
 *  (`$[0].prop` bei Listen, sonst `$.prop`). Damit die Vorschläge zum
 *  case-sensitiven camelCase-Server-Output passen. */
export function jsonPathSuggestions(
  returnType: string | undefined | null,
  props: { propertyName: string }[] | undefined | null,
): string[] {
  const opts: string[] = ['$'];
  const list = isListReturnType(returnType);
  const prefix = list ? '$[0].' : '$.';
  if (list) opts.push('$[0]');
  for (const p of props ?? []) opts.push(`${prefix}${toCamelCase(p.propertyName)}`);
  return opts;
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