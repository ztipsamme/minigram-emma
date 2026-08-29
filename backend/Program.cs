/*
Program.cs — MinGram API
ASP.NET Core Minimal API: endpoints definieras direkt här, inga controllers.

Starta lokalt:  dotnet run
Swagger UI:     https://localhost:{port}/swagger
*/
using backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new ImageService(config, env.IsDevelopment());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.ConfigureCors();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("MinGramPolicy");

var isDev = app.Environment.IsDevelopment();
var devTestRole = builder.Configuration["DEV_TEST_ROLE"];

var imageService = app.Services.GetRequiredService<ImageService>();

app.MapImageEndpoints(imageService, isDev, devTestRole);

app.Run();