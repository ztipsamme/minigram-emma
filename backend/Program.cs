/*
Program.cs — MinGram API
ASP.NET Core Minimal API: endpoints definieras direkt här, inga controllers.

Starta lokalt:  dotnet run
Swagger UI:     https://localhost:{port}/swagger
*/
using backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ImageService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.ConfigureCors();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("MinGramPolicy");

var isDev = app.Environment.IsDevelopment();

app.MapImageEndpoints(isDev);

app.Run();