using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CoreServices;
using CoreServices.Services;
using MyOpcUaApi.Controllers;
using MyOpcUaApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;
var connectionString = configuration.GetConnectionString("DefaultConnection");

// ? Serviços base
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddMemoryCache();

// ? Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MyOpcUa API",
        Version = "v1",
        Description = "API para comunicação com servidores OPC UA.",
        Contact = new OpenApiContact
        {
            Name = "Seu Nome",
            Email = "seuemail@example.com"
        }
    });
});

// ? OPC UA
builder.Services.AddSingleton<OpcUaClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new OpcUaClient(config, msg => Console.WriteLine(msg));
});

// ? Monitoramento Status Manager
builder.Services.AddSingleton<MonitoramentoStatusManager>();

// ? DatabaseManager (tem que vir antes do ApplicationManager)
builder.Services.AddSingleton<DatabaseManager>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connString = config.GetConnectionString("DefaultConnection") ?? string.Empty;

    if (string.IsNullOrEmpty(connString))
    {
        Console.WriteLine("[ERRO] String de conexão do banco de dados não encontrada!");
    }
    else
    {
        Console.WriteLine($"[DEBUG] DatabaseManager registrado com conexão: {connString}");
    }

    return new DatabaseManager(connString);
});

// ? ApplicationManager (passa MonitoramentoService como null inicialmente)
builder.Services.AddSingleton<ApplicationManager>(sp =>
{
    var opcUaClient = sp.GetRequiredService<OpcUaClient>();
    var databaseManager = sp.GetRequiredService<DatabaseManager>();
    return new ApplicationManager(opcUaClient, databaseManager); // ?? monitoramento será setado depois
});

// ? MonitoramentoService (depois que ApplicationManager estiver disponível)
builder.Services.AddSingleton<MonitoramentoService>(sp =>
{
    var opcUaClient = sp.GetRequiredService<OpcUaClient>();
    var appManager = sp.GetRequiredService<ApplicationManager>();
    var monitoramentoService = new MonitoramentoService(opcUaClient, appManager);

    appManager.SetMonitoramentoService(monitoramentoService); // ?? vínculo circular resolvido com setter
    return monitoramentoService;
});

// ? Serviço de Background
builder.Services.AddHostedService<MonitoramentoBackgroundService>();

// ? Outros
builder.Services.AddScoped<DatabaseOrchestratorService>();

// ? CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

// ? Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MyOpcUa API v1");
    c.RoutePrefix = "swagger";
});

// ? Middlewares
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

// ? URL explícita
try
{
    var url = "http://0.0.0.0:5000";
    app.Urls.Add(url);
    Console.WriteLine($"[INFO] API iniciada e rodando em {url}");
}
catch (Exception ex)
{
    Console.WriteLine($"[ERRO] Falha ao iniciar a API: {ex.Message}");
}

app.Run();
