using Hrpc.Grpc;
using Hrpc.Hrpc;
using Hrpc.Services;
using HrpcHub.Extensions;
using HrpcWebSocket;
using HrpcDeveloperUi;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddGrpc();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddHrpc(new HrpcOptions()
{
    EnableDetailedErrors = builder.Environment.IsDevelopment(),
    UseMessagePack = true,
    UseSignalR = true,
    MaximumParallelInvocationsPerClient = 100,
    MaximumReceiveMessageSize = 102400,
    StreamBufferCapacity = 100,
    RateLimitPermitLimit = builder.Environment.IsProduction() ? 50 : 0,
    RateLimitWindowSeconds = 10
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
app.UseRateLimiter();

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseDefaultFiles();

app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.UseHrpc();

app.MapRazorPages();

app.MapHrpcDeveloperUi();

app.UseWebSockets();
app.UseHrpcWebSocket("/hrpcws");

app.MapHub<HrpcHub.Hub.HrpcHub>("/Hrpc");

app.MapGrpcService<CustomerGrpcService>();

app.Run();
