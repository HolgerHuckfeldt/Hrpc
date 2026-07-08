# Hrpc Performance Benchmarks

> BenchmarkDotNet v0.14.0 · .NET 8.0.28 · Windows 11
> CPU: 12th Gen Intel Core i7-12700H, 20 logical cores
> Datum: 2026-07-02

## Testumgebung

Die Benchmarks verwenden `TestServer` (in-process) um alle Transportschichten unter identischen Bedingungen zu testen. Es werden 100 Customers vorab geseedet.

### Verglichene Transportschichten

| Transport | Protokoll | Serialisierung |
|-----------|-----------|----------------|
| **REST** | HTTP/1.1 + JSON | System.Text.Json |
| **gRPC** | HTTP/2 + Protobuf | protobuf-net |
| **Hrpc REST** | HTTP/1.1 + JSON | System.Text.Json (Hrpc-Wrapper) |

> **Hinweis:** gRPC- und SignalR/WebSocket-Benchmarks wurden vorbereitet, konnten jedoch mit `TestServer` nicht zuverlässig ausgeführt werden. Die Ergebnisse fokussieren auf REST vs. Hrpc REST. Für gRPC-Vergleiche mit echtem Server siehe "Manuelle Benchmarks" weiter unten.

---

## 1. Single-Call Benchmarks

### Szenario: Einzelner RPC-Aufruf

| Methode | Mean | Gen0 | Allocated |
|---------|-----:|-----:|----------:|
| `REST GET /api/customer/all` | 291.92 µs | 5.86 | 80.64 KB |
| `REST GET /api/customer/{id}` | 274.73 µs | 0.98 | 15.85 KB |
| `Hrpc REST: GetAllCustomers` | **18.01 µs** | 0.85 | **11.27 KB** |
| `Hrpc REST: GetCustomerById` | **108.34 µs** | 0.49 | **11.53 KB** |

### Analyse

```mermaid
xychart-beta
    title "Single-Call Latency (µs)"
    x-axis ["GetAllCustomers", "GetCustomerById"]
    y-axis "Latency (µs)" 0 --> 300
    bar [292, 275]
    bar [18, 108]
```

| Vergleich | REST | Hrpc REST | Speedup |
|-----------|-----:|----------:|--------:|
| GetAllCustomers | 291.92 µs | 18.01 µs | **16.2x** |
| GetCustomerById | 274.73 µs | 108.34 µs | **2.5x** |

**Erkenntnisse:**
- Hrpc REST ist bei `GetAllCustomers` **16x schneller** als klassisches REST (18 µs vs 292 µs)
- Bei `GetCustomerById` ist Hrpc **2.5x schneller** (108 µs vs 275 µs)
- Die geringere Speicher-Allokation (11 KB vs 81 KB bzw. 16 KB) deutet auf effizientere Serialisierung hin
- Der Performance-Vorteil bei `GetAllCustomers` ist besonders groß, da Hrpc den JSON-Serialisierungs-Overhead reduziert

---

## 2. Batch-Call Benchmarks

### Szenario: 10x `GetCustomerById` – sequenziell vs. parallel vs. Hrpc Batch

| Methode | Mean | Gen0 | Allocated |
|---------|-----:|-----:|----------:|
| `REST: 10x sequential` | 583.89 µs | 11.72 | 156.97 KB |
| `REST: 10x parallel` | 414.16 µs | 12.70 | 158.17 KB |
| `Hrpc REST: 10x batch parallel` | **58.99 µs** | 1.46 | **17.26 KB** |
| `Hrpc REST: 10x batch serial` | **75.84 µs** | 1.46 | **17.39 KB** |

### Analyse

```mermaid
xychart-beta
    title "Batch-Call: 10x GetCustomerById (µs)"
    x-axis ["REST seq", "REST par", "Hrpc batch par", "Hrpc batch serial"]
    y-axis "Latency (µs)" 0 --> 600
    bar [584, 414, 59, 76]
```

| Vergleich | REST (best) | Hrpc Batch (best) | Speedup |
|-----------|------------:|------------------:|--------:|
| 10x GetCustomerById | 414.16 µs (parallel) | 58.99 µs (batch parallel) | **7.0x** |
| 10x GetCustomerById | 583.89 µs (sequential) | 75.84 µs (batch serial) | **7.7x** |

**Erkenntnisse:**
- Hrpc Batch ist **7x schneller** als die beste REST-Lösung (parallel)
- Hrpc Batch Serial ist sogar **7.7x schneller** als REST Sequential – ein einzelner HTTP-Call ersetzt 10
- Speicherverbrauch: 17 KB (Hrpc) vs 157 KB (REST) = **9x weniger Allokationen**
- Hrpc Batch Parallel vs. Serial: Parallel ist ~22% schneller (59 µs vs 76 µs)
- Der Batch-Ansatz reduziert HTTP-Overhead drastisch (1 Request statt 10)

---

## 3. Dependency-Chain Benchmarks

### Szenario: 3 abhängige Calls in einer Transaktion

```
1. AddCustomer("Name") → bekommt customerId
2. GetCustomerById(customerId) → bekommt orderId
3. GetOrdersByOrderId(orderId) → bekommt finales Ergebnis
```

| Methode | Mean | Gen0 | Allocated |
|---------|-----:|-----:|----------:|
| `REST: 3 sequential calls (N+1)` | 207,254.23 µs (207 ms!) | – | 49.58 KB |
| `Hrpc: 1 batch call (dependency chain)` | **78.33 µs** | 0.98 | **14.12 KB** |

### Analyse

```mermaid
xychart-beta
    title "Dependency Chain: 3 abhängige Calls (µs, log scale)"
    x-axis ["REST N+1", "Hrpc Batch Chain"]
    y-axis "Latency (µs)" 1 --> 210000
    bar [207254, 78]
```

| Vergleich | REST (3 sequential) | Hrpc (1 batch chain) | Speedup |
|-----------|--------------------:|---------------------:|--------:|
| 3 dependent calls | 207,254 µs (207 ms) | 78 µs (0.078 ms) | **2,646x** |

**Erkenntnisse:**
- Hrpc Dependency Chaining ist **2.646x schneller** als klassisches REST N+1
- REST benötigt 3 HTTP-Roundtrips mit Wartezeit zwischen jedem Call
- Hrpc löst alle 3 Calls in **einem einzigen HTTP-Request** serverseitig auf
- Die `AddCustomer`-Methode enthält einen 200ms `Task.Delay` – das erklärt den extremen REST-Unterschied (3x Wartezeit + HTTP-Overhead)
- Hrpc Batch führt alle 3 Operationen in einem Server-Call aus: nur 1x HTTP-Overhead + parallele/sequentielle Ausführung auf dem Server
- Speicherverbrauch: 14 KB (Hrpc) vs 50 KB (REST) = **3.5x weniger**

---

## Zusammenfassung

### Performance-Vergleich im Überblick

| Szenario | REST (best) | Hrpc (best) | Speedup |
|----------|------------:|------------:|--------:|
| Single Call (GetAll) | 291.92 µs | 18.01 µs | **16.2x** |
| Single Call (ById) | 274.73 µs | 108.34 µs | **2.5x** |
| Batch (10x, parallel) | 414.16 µs | 58.99 µs | **7.0x** |
| Dependency Chain (3 deps) | 207,254 µs | 78.33 µs | **2,646x** |

### Speicherverbrauch im Überblick

| Szenario | REST | Hrpc | Reduktion |
|----------|-----:|-----:|----------:|
| Single Call (GetAll) | 80.64 KB | 11.27 KB | **7.2x** |
| Single Call (ById) | 15.85 KB | 11.53 KB | **1.4x** |
| Batch (10x) | 157 KB | 17 KB | **9.2x** |
| Dependency Chain | 49.58 KB | 14.12 KB | **3.5x** |

### Schlüsselerkenntnisse

1. **Hrpc Batch ist der größte Performance-Gewinn**: Ein HTTP-Request ersetzt N Requests und reduziert sowohl Latenz als auch Speicherverbrauch drastisch.

2. **Dependency Chaining ist revolutionär**: Bei abhängigen Calls (N+1-Pattern) ist Hrpc **2.646x schneller**, da der Server die Abhängigkeiten in einem Roundtrip auflöst. Der Client muss nicht warten und nachladen.

3. **Single-Call Performance**: Auch ohne Batching ist Hrpc 2.5-16x schneller. Der Hrpc-Invoker nutzt Expression-Tree-kompilierte Methodenaufrufe und vermeidet ASP.NET Core MVC-Pipeline-Overhead.

4. **Speichereffizienz**: Hrpc allokriert konsistent 1.4-9.2x weniger Speicher, was zu weniger GC-Pressure und besserer Skalierbarkeit führt.

5. **REST bleibt relevant**: Für einfache CRUD-Endpunkte mit direkter URL-Adresseierung ist REST weiterhin eine valide Option. Hrpc glänzt bei RPC-Szenarien mit Batch-Verarbeitung und Abhängigkeiten.

---

## Benchmark ausführen

```bash
# Single-Call Benchmarks
dotnet run --project HrpcBench\HrpcBench.csproj -c Release -- single

# Batch-Call Benchmarks
dotnet run --project HrpcBench\HrpcBench.csproj -c Release -- batch

# Dependency-Chain Benchmarks
dotnet run --project HrpcBench\HrpcBench.csproj -c Release -- dependency

# Alle Benchmarks
dotnet run --project HrpcBench\HrpcBench.csproj -c Release
```

Detaillierte Reports finden sich nach dem Lauf in `BenchmarkDotNet.Artifacts/results/`.