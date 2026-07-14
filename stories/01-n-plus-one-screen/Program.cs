// =============================================================================
//  Story 01 — The N+1 Screen  (standalone solution, F5 → DevUI)
//
//    One Order detail page, six dependent reads, five services.
//    The Trame thesis: the client declares WHAT depends on WHAT;
//    the server resolves the graph in one roundtrip.
//
//    In Visual Studio: open Story01.sln, press F5 → browser lands in the DevUI
//    at /Trame, with the six Story-01 controllers (Order, Customer, OrderLine,
//    Article, Address, Stock) ready to call. See README.md for the narrative
//    and the one-batch call that replaces six sequential roundtrips.
// =============================================================================

using TrameHub.Extensions;
using TrameServer;

var builder = WebApplication.CreateBuilder(args);

// DevUI-Bundles aus dem benachbarten TrameDeveloperUi-Projekt im Dev-Modus ausliefern.
// (Production/Publish braucht stattdessen das StaticWebAssets-Manifest + UseStaticFiles.)
builder.WebHost.UseStaticWebAssets();

builder.Services.AddTrame(new TrameOptions
{
    EnableDetailedErrors = builder.Environment.IsDevelopment(),
    RateLimitPermitLimit = 0, // Demo ohne Rate-Limit — South-Bound-Story
});

var app = builder.Build();

// Static-Web-Assets (Dev) stellt die DevUI-Bundles aus dem benachbarten
// TrameDeveloperUi-Projekt bereit; UseStaticFiles dient sie tatsächlich aus.
app.UseStaticFiles();

app.UseRouting();

// WebSocket (Default-Kanal) + Controller-Registrierung (Auto-Discovery übernimmt
// die Story-Controller aus Domain.cs) in einem Aufruf.
app.UseTrameTransports();

// REST + Developer-UI in einem Aufruf. (SignalR ist hier off — Story 03 zeigt den
// dritten Wire; Story 01 braucht nur REST + WS, beide über UseTrameTransports/MapTrame.)
app.MapTrame();

// F5-Komfort: Browser-Start auf / landet direkt in der DevUI.
app.MapGet("/", ctx => { ctx.Response.Redirect("/Trame"); return Task.CompletedTask; });

app.Run();