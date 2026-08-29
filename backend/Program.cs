/*
Program.cs — MinGram API
ASP.NET Core Minimal API: endpoints definieras direkt här, inga controllers.

Starta lokalt:  dotnet run
Swagger UI:     https://localhost:{port}/swagger

Frontend (minigram-app-emma) pratar med API:t via publika HTTPS-URL:en.
VNet/subnet styr framför allt API → Storage (backend-subnet), inte webbläsaren.
CORS måste tillåta frontend-URL:en.
*/

using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("MinGramPolicy", policy =>
    {
        var origins = builder.Configuration
                             .GetSection("AllowedOrigins")
                             .Get<string[]>() ?? [];
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("MinGramPolicy");

var rollMappningJson = builder.Configuration["RollMappningJson"];
var rollMappning =
    string.IsNullOrEmpty(rollMappningJson)
        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        : JsonSerializer.Deserialize<Dictionary<string, string>>(rollMappningJson)
          ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

rollMappning = new Dictionary<string, string>(rollMappning, StringComparer.OrdinalIgnoreCase);

var storageConn = builder.Configuration["AzureStorageConnectionString"];
var containerNamn = builder.Configuration["AzureStorageContainer"] ?? "bilder";
BlobContainerClient? blobContainer = null;

if (!string.IsNullOrWhiteSpace(storageConn))
{
    var blobService = new BlobServiceClient(storageConn);
    blobContainer = blobService.GetBlobContainerClient(containerNamn);
}

var bilder = new List<Bild>
{
    new(1, "demo.jpg", "Demobild — ersätt med din egen", ["demo", "placeholder"],
        "https://placehold.co/400x300?text=MinGram")
};
var nastaBildId = 2;
// Blobnamn per bild-id — webbläsaren får inte gå direkt till storage (firewall Deny).
var blobNamnPerId = new Dictionary<int, string>();

string BildUrl(HttpRequest req, int id)
    => $"{req.Scheme}://{req.Host}/bilder/{id}/fil";

app.MapGet("/bilder", () => bilder)
   .WithName("HamtaBilder")
   .WithSummary("Hämta alla bilder — alla roller");

app.MapGet("/bilder/{id:int}", (int id) =>
{
    var b = bilder.FirstOrDefault(b => b.Id == id);
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
    if (!HarBehorighet(HamtaRoll(req), "Fotograf")) return Results.StatusCode(403);
    var b = new Bild(nastaBildId++, ny.Namn, ny.Caption, ny.Taggar ?? [], ny.Url);
    bilder.Add(b);
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
    if (!HarBehorighet(HamtaRoll(req), "Fotograf") || !HarBehorighet(HamtaRoll(req), "Admin")) return Results.StatusCode(403);


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
        bilder.Add(b);
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
    if (!HarBehorighet(HamtaRoll(req), "Fotograf")) return Results.StatusCode(403);
    var index = bilder.FindIndex(b => b.Id == id);
    if (index < 0) return Results.NotFound();
    bilder[index] = bilder[index] with
    {
        Caption = update.Caption ?? bilder[index].Caption,
        Taggar = update.Taggar ?? bilder[index].Taggar
    };
    return Results.Ok(bilder[index]);
})
.WithName("UppdateraBild")
.WithSummary("Uppdatera bild — kräver Fotograf eller Admin");

app.MapDelete("/bilder/{id:int}", async (int id, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Admin")) return Results.StatusCode(403);
    var b = bilder.FirstOrDefault(b => b.Id == id);
    if (b is null) return Results.NotFound();
    bilder.Remove(b);

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

string? HamtaEmail(HttpRequest request)
{
    // Workaround: frontend skickar e-post efter egen Easy Auth-login
    // (när API:t kör AllowAnonymous / auth av).
    var forwarded = request.Headers["X-User-Email"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwarded) && forwarded.Contains('@'))
        return forwarded.Trim();

    var header = request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();
    if (string.IsNullOrEmpty(header)) return null;

    try
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(header));
        using var doc = JsonDocument.Parse(json);

        foreach (var claim in doc.RootElement.GetProperty("claims").EnumerateArray())
        {
            var typ = claim.TryGetProperty("typ", out var t1) ? t1.GetString()
                    : claim.TryGetProperty("type", out var t2) ? t2.GetString()
                    : null;

            if (typ is "roles")
                continue;

            if (typ is "preferred_username"
                or "upn"
                or "emails"
                or "email"
                or "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn"
                or "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"
                or "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")
            {
                var val = claim.GetProperty("val").GetString();
                if (!string.IsNullOrWhiteSpace(val) && val.Contains('@'))
                    return val;
            }
        }
    }
    catch { }

    return null;
}

string HamtaRoll(HttpRequest request)
{
    var header = request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();

    if (!string.IsNullOrEmpty(header))
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(header));
            using var doc = JsonDocument.Parse(json);

            foreach (var claim in doc.RootElement.GetProperty("claims").EnumerateArray())
            {
                var typ = claim.TryGetProperty("typ", out var t1) ? t1.GetString()
                        : claim.TryGetProperty("type", out var t2) ? t2.GetString()
                        : null;

                if (typ == "roles")
                    return claim.GetProperty("val").GetString() ?? "Betraktare";
            }
        }
        catch
        {
            // fall through till e-postmappning
        }
    }

    var email = HamtaEmail(request);
    if (email != null && rollMappning.TryGetValue(email, out var mappad))
        return mappad;

    if (string.IsNullOrEmpty(header) && app.Environment.IsDevelopment())
        return "Admin";

    return "Betraktare";
}

bool HarBehorighet(string roll, string kravRoll) => (roll, kravRoll) switch
{
    (_, "Betraktare") => true,
    ("Fotograf" or "Admin", "Fotograf") => true,
    ("Admin", "Admin") => true,
    _ => false
};

record Bild(int Id, string Namn, string Caption, List<string> Taggar, string Url);
record NyBild(string Namn, string Caption, List<string>? Taggar, string Url);
record BildUpdate(string? Caption, List<string>? Taggar);
