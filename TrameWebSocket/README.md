# Trame WebSocket-Transport

Dieser Transport verwendet ausschließlich **Standard-WebSockets (RFC 6455)** und **JSON-Textnachrichten**. Er ist damit besonders einfach in andere Sprachen (Java, JavaScript, Python, Go, Rust, …) integrierbar und hat weniger Overhead als SignalR.

## Endpunkt

```
ws://<host>/tramews      (HTTP)
wss://<host>/tramews     (HTTPS)
```

In der .NET-Anwendung wird der Endpunkt automatisch mit

```csharp
app.UseWebSockets();
app.UseTrameWebSocket("/tramews");
```

registriert.

## Protokoll

Jede Nachricht ist ein JSON-Objekt, das entweder einen Einzel-Request oder einen Multi-Request repräsentiert.

### Einzelner Request

```json
{
  "controller": "Customer",
  "method": "GetCustomerById",
  "stringData": "[{\"parameterName\":\"id\",\"data\":\"1\"}]",
  "id": "my-request-id"
}
```

Antwort:

```json
{
  "id": "my-request-id",
  "code": 200,
  "data": "{\"id\":1,\"name\":\"Max\"}",
  "exposedDependencies": null
}
```

### Multi-Request

```json
{
  "mode": 0,
  "requests": [
    {
      "controller": "Customer",
      "method": "AddCustomer",
      "stringData": "[{\"parameterName\":\"name\",\"data\":\"\\\"Max\\\"\"}]",
      "id": "create",
      "dependencyMapping": { "newId": "$" }
    },
    {
      "controller": "Customer",
      "method": "GetCustomerById",
      "stringData": "[{\"parameterName\":\"id\",\"data\":\"@newId\"}]",
      "id": "read"
    }
  ]
}
```

`mode`: `0` = parallel, `1` = serial

Bei `mode: 1` werden die Requests nacheinander ausgeführt und Aliase (`@alias`) aus vorherigen Responses aufgelöst.

Antwort:

```json
[
  { "id": "create", "code": 200, "data": "1", "exposedDependencies": { "newId": "1" } },
  { "id": "read", "code": 200, "data": "..." }
]
```

## Java-Beispiel

```java
import java.net.URI;
import org.java_websocket.client.WebSocketClient;
import org.java_websocket.handshake.ServerHandshake;

public class TrameWebSocketClient extends WebSocketClient {
    public TrameWebSocketClient(URI serverUri) {
        super(serverUri);
    }

    @Override
    public void onOpen(ServerHandshake handshake) {
        String request = "{\"controller\":\"Customer\",\"method\":\"GetAllCustomers\",\"id\":\"1\"}";
        send(request);
    }

    @Override
    public void onMessage(String message) {
        System.out.println("Response: " + message);
    }

    @Override
    public void onClose(int code, String reason, boolean remote) { }

    @Override
    public void onError(Exception ex) { ex.printStackTrace(); }
}
```

## JavaScript-Beispiel

```javascript
const ws = new WebSocket('ws://localhost:5000/tramews');

ws.onopen = () => {
    ws.send(JSON.stringify({
        controller: 'Customer',
        method: 'GetAllCustomers',
        id: '1'
    }));
};

ws.onmessage = (event) => {
    console.log('Response:', JSON.parse(event.data));
};
```

## Python-Beispiel

```python
import asyncio
import websockets
import json

async def call():
    uri = "ws://localhost:5000/tramews"
    async with websockets.connect(uri) as ws:
        request = {
            "controller": "Customer",
            "method": "GetAllCustomers",
            "id": "1"
        }
        await ws.send(json.dumps(request))
        response = await ws.recv()
        print(response)

asyncio.run(call())
```

## .NET-Client

```csharp
await using var client = new TrameWebSocketClient("https://localhost:5001");
await client.ConnectAsync();

var response = await client.Call(new TrameRequest
{
    Controller = "Customer",
    Method = "GetAllCustomers"
});
```

## Vorteile gegenüber SignalR

- Kein proprietäres Protokoll
- Geringerer Overhead (kein Hub-Negotiation, kein MessagePack nötig)
- Einfachere Implementierung in Nicht-.NET-Sprachen
- Direkte Kontrolle über Verbindungslebenszyklus

## Einschränkungen

- Aktuell nur JSON (kein MessagePack)
- Keine automatische Wiederverbindung (muss clientseitig implementiert werden)
- Kein zentrales Authentifizierungs-Handshake; Token können z. B. im Query-String übergeben werden
