using Hrpc.Hrpc;
using Hrpc.Services;
using HrpcHub.Extensions;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHrpc(new HrpcOptions()
{
    EnableDetailedErrors = true,
    UseMessagePack = true,
    UseSignalR = true,
    MaximumParallelInvocationsPerClient = 100,
    MaximumReceiveMessageSize = 102400,
    StreamBufferCapacity = 100
});

builder.Services.AddSingleton<CustomerService>();
builder.Services.AddRazorPages();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.UseHrpc();


app.UseDefaultFiles();
app.UseRouting();
app.MapRazorPages();


// Konfiguriere statische Dateien für den /hrpc-Pfad
//app.UseStaticFiles(new StaticFileOptions
//{
//    FileProvider = new PhysicalFileProvider(
//        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "developer")),
//    RequestPath = "/developer"
    
//});

app.UseStaticFiles();

app.MapGet("/developer", ctx =>
{

    ctx.Response.Redirect("/_content/HrpcDeveloperUi/developer/index.html");
    return Task.CompletedTask;
});

app.MapHub<HrpcHub.Hub.HrpcHub>("/Hrpc");
//app.MapGrpcService<Hrpc.Grpc.HrpcService>();

app.Run();
