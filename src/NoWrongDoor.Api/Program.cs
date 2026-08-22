using System.Text.Json.Serialization;
using NoWrongDoor.Adapters;
using NoWrongDoor.Core.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddHttpClient<IResidentSource, ResidentIndexAdapter>(ResidentIndexAdapter.HttpClientName, client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ResidentIndex:BaseUrl"] ?? "http://127.0.0.1:8081/");
});

builder.Services.AddHttpClient<IBenefitsSource, BenefitsRegisterAdapter>(BenefitsRegisterAdapter.HttpClientName, client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BenefitsRegister:BaseUrl"] ?? "http://127.0.0.1:8082/");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
