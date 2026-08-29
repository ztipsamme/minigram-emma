/*
Program.cs — MinGram API
ASP.NET Core Minimal API: endpoints definieras direkt här, inga controllers.

Starta lokalt:  dotnet run
Swagger UI:     https://localhost:{port}/swagger
*/

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.ConfigureCors();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("MinGramPolicy");

var rollMappning = RoleMapping.Load(builder.Configuration);

var storageConn = builder.Configuration["AzureStorageConnectionString"];
var containerNamn = builder.Configuration["AzureStorageContainer"] ?? "bilder";
BlobContainerClient? blobContainer = null;

if (!string.IsNullOrWhiteSpace(storageConn))
{
    var blobService = new BlobServiceClient(storageConn);
    blobContainer = blobService.GetBlobContainerClient(containerNamn);
}


var nastaBildId = 2;
// Blobnamn per bild-id — webbläsaren får inte gå direkt till storage (firewall Deny).
var blobNamnPerId = new Dictionary<int, string>();

string BildUrl(HttpRequest req, int id)
    => $"{req.Scheme}://{req.Host}/bilder/{id}/fil";

app.MapGet("/bilder", () => MockImages.Bilder)
   .WithName("HamtaBilder")
   .WithSummary("Hämta alla bilder — alla roller");

app.MapGet("/bilder/{id:int}", (int id) =>
{
    var b = MockImages.Bilder.FirstOrDefault(b => b.Id == id);
    return b is not null ? Results.Ok(b) : Results.NotFound();
})
.WithName("HamtaBild")
.WithSummary("Hämta en specifik bild — alla roller");

app.MapGet("/bilder/{id:int}/fil", async (int id) =>
{
    if (blobContainer is null)
        return Results.Problem("Blob Storage är inte konfigurerat.");

    if (!blobNamnPerId.TryGetValue(id, out var blobNamn))
        return Results.NotFound();

    var blob = blobContainer.GetBlobClient(blobNamn);
    if (!await blob.ExistsAsync())
        return Results.NotFound();

    var props = await blob.GetPropertiesAsync();
    var download = await blob.DownloadStreamingAsync();
    var contentType = string.IsNullOrWhiteSpace(props.Value.ContentType)
        ? "image/jpeg"
        : props.Value.ContentType;

    return Results.File(download.Value.Content, contentType);
})
.WithName("HamtaBildFil")
.WithSummary("Streama bildfil via API (så storage kan vara stängd utåt)");

app.MapPost("/bilder", (NyBild ny, HttpRequest req) =>
{
    var roll = RoleMapping.GetRole(req, rollMappning, app.Environment);
    if (!RoleMapping.HasPermission(roll, "Fotograf") && !RoleMapping.HasPermission(roll, "Admin"))
        return Results.StatusCode(403);

    var b = new Bild(nastaBildId++, ny.Namn, ny.Caption, ny.Taggar ?? [], ny.Url);
    MockImages.Bilder.Add(b);
    return Results.Created($"/bilder/{b.Id}", b);
})
.WithName("LaddaUppBild")
.WithSummary("Lägg till bild via URL — kräver Fotograf eller Admin");

app.MapPost("/bilder/uppladdning", async (
    HttpRequest req,
    IFormFile? fil,
    [FromForm] string? caption,
    [FromForm] string? namn,
    [FromForm] string? taggar) =>
{
    var roll = RoleMapping.GetRole(req, rollMappning, app.Environment);
    if (!RoleMapping.HasPermission(roll, "Fotograf") && !RoleMapping.HasPermission(roll, "Admin"))
        return Results.StatusCode(403);

    if (blobContainer is null)
        return Results.Json(new { error = "Blob Storage är inte konfigurerat." }, statusCode: 500);

    if (fil is null || fil.Length == 0)
        return Results.BadRequest(new { error = "Skicka en fil i fältet 'fil'." });

    try
    {
        caption = string.IsNullOrWhiteSpace(caption) ? fil.FileName : caption;
        namn = string.IsNullOrWhiteSpace(namn) ? fil.FileName : namn;

        var taggLista = string.IsNullOrWhiteSpace(taggar)
            ? new List<string>()
            : taggar.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

        var blobNamn = $"{Guid.NewGuid():N}-{Path.GetFileName(fil.FileName)}";
        var blob = blobContainer.GetBlobClient(blobNamn);

        var contentType = string.IsNullOrWhiteSpace(fil.ContentType)
            ? "image/jpeg"
            : fil.ContentType;

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        await using (var stream = fil.OpenReadStream())
        {
            await blob.UploadAsync(stream, uploadOptions);
        }

        var id = nastaBildId++;
        blobNamnPerId[id] = blobNamn;
        var b = new Bild(id, namn, caption, taggLista, BildUrl(req, id));
        MockImages.Bilder.Add(b);
        return Results.Created($"/bilder/{b.Id}", b);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.GetType().Name, message = ex.Message }, statusCode: 500);
    }
})
.DisableAntiforgery()
.WithName("LaddaUppBildFil")
.WithSummary("Ladda upp bildfil till Blob Storage — kräver Fotograf eller Admin");

app.MapPut("/bilder/{id:int}", (int id, BildUpdate update, HttpRequest req) =>
{
    var roll = RoleMapping.GetRole(req, rollMappning, app.Environment);
    if (!RoleMapping.HasPermission(roll, "Fotograf")) return Results.StatusCode(403);
    var index = MockImages.Bilder.FindIndex(b => b.Id == id);
    if (index < 0) return Results.NotFound();
    MockImages.Bilder[index] = MockImages.Bilder[index] with
    {
        Caption = update.Caption ?? MockImages.Bilder[index].Caption,
        Taggar = update.Taggar ?? MockImages.Bilder[index].Taggar
    };
    return Results.Ok(MockImages.Bilder[index]);
})
.WithName("UppdateraBild")
.WithSummary("Uppdatera bild — kräver Fotograf eller Admin");

app.MapDelete("/bilder/{id:int}", async (int id, HttpRequest req) =>
{
    var roll = RoleMapping.GetRole(req, rollMappning, app.Environment);
    if (!RoleMapping.HasPermission(roll, "Admin")) return Results.StatusCode(403);
    var b = MockImages.Bilder.FirstOrDefault(b => b.Id == id);
    if (b is null) return Results.NotFound();
    MockImages.Bilder.Remove(b);

    if (blobNamnPerId.Remove(id, out var blobNamn) && blobContainer is not null)
    {
        try { await blobContainer.DeleteBlobIfExistsAsync(blobNamn); }
        catch { /* radera metadata även om blob misslyckas */ }
    }

    return Results.NoContent();
})
.WithName("RaderaBild")
.WithSummary("Radera bild — kräver Admin");

app.Run();