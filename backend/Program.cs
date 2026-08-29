/*
Program.cs — MinGram API
ASP.NET Core Minimal API: endpoints definieras direkt här, inga controllers.

Starta lokalt:  dotnet run
Swagger UI:     https://localhost:{port}/swagger
*/

using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using backend.Services;

var builder = WebApplication.CreateBuilder(args);

var storageAccountName = "stminigramemma";
var containerName = "bilder";

var blobServiceClient = new BlobServiceClient(
    new Uri($"https://{storageAccountName}.blob.core.windows.net"),
    new DefaultAzureCredential());

var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

builder.Services.AddSingleton(new ImageStorageService(containerClient));
builder.Services.AddSingleton<ImageService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.ConfigureCors();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("MinGramPolicy");

var imageService = app.Services.GetRequiredService<ImageService>();
var isDev = app.Environment.IsDevelopment();

app.MapImageEndpoints(imageService, isDev);

app.Run();