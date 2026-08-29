/*
Program.cs — MinGram API
ASP.NET Core Minimal API: endpoints definieras direkt här, inga controllers.

Starta lokalt:  dotnet run
Swagger UI:     https://localhost:{port}/swagger

v35 — Azure-konfiguration (görs i portalen, inte i koden):
1. CORS: App Service → API → CORS → lägg till din frontend-URL
2. Easy Auth: App Service → Authentication → Add identity provider → Microsoft
   Välj din Entra ID-tenant. Alla anrop kräver nu inloggning.
3. App-roller i Entra ID: gå till App registrations → din app → App roles
   Skapa rollerna Betraktare, Fotograf, Admin.
   Tilldela dem till dina Entra ID-användare under Enterprise applications.

Bilder lagras som URL:er — ladda upp till Azure Blob Storage och skicka URL:en hit.
*/

using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

var builder = WebApplication.CreateBuilder(args);

var storageAccountName = "stminigramemma";
var containerName = "bilder";

var blobServiceClient = new BlobServiceClient(
    new Uri($"https://{storageAccountName}.blob.core.windows.net"),
    new DefaultAzureCredential());

var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

/*
CORS — hanteras primärt i Azure Portal: App Service → API → CORS
Lägg till din frontend-URL där, så slipper du ändra och redeploya koden.
Den här koden hanterar CORS lokalt under utveckling.
*/
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

/* 
-------------------------------------------------------
In-memory datastore med seed-data
Datan nollställs vid omstart — en riktig app lagrar bilder i Blob Storage
------------------------------------------------------- 
*/




// Alla roller får se bilder
app.MapGet("/bilder", async () =>
{
    try
    {
        var bilder = new List<Bild>();

        var options = new GetBlobsOptions
        {
            Traits = BlobTraits.Metadata,
            States = BlobStates.None
        };

        await foreach (var blob in containerClient.GetBlobsAsync(options))
        {
            var bild = CreateBildFromBlob(blob);

            if (bild is not null)
            {
                bilder.Add(bild);
            }
        }

        return Results.Ok(bilder);
    }
    catch (Exception ex)
{
    Console.WriteLine(ex.ToString());

    return Results.Problem(
        statusCode: 500,
        title: "Error accessing Blob Storage",
        detail: ex.Message);

    }
})
.WithName("HamtaBilder")
.WithSummary("Get all images — all roles");


// =======================================================
// GET /bilder/{id}
// =======================================================
// All roles can read image metadata.
// =======================================================

app.MapGet("/bilder/{id:int}", async (int id) =>
{
    var blobClient = await FindImageBlob(id);

    if (blobClient is null)
        return Results.NotFound();

    var properties = await blobClient.GetPropertiesAsync();
    var metadata = properties.Value.Metadata;

    var originalName =
        metadata.TryGetValue("originalName", out var name)
            ? name
            : blobClient.Name;

    var caption =
        metadata.TryGetValue("caption", out var captionValue)
            ? captionValue
            : "";

    var tags =
        metadata.TryGetValue("tags", out var tagsValue)
            ? JsonSerializer.Deserialize<List<string>>(tagsValue) ?? []
            : [];

    var bild = new Bild(
        id,
        originalName,
        caption,
        tags,
        $"/bilder/{id}/image");

    return Results.Ok(bild);
})
.WithName("HamtaBild")
.WithSummary("Get a specific image — all roles");

// =======================================================
// GET /bilder/{id}/image
// =======================================================
// All roles can retrieve the actual image.
// The Blob remains private.
// =======================================================

app.MapGet("/bilder/{id:int}/image", async (int id) =>
{
    var blobClient = await FindImageBlob(id);

    if (blobClient is null)
        return Results.NotFound();

    var response = await blobClient.DownloadStreamingAsync();

    var contentType =
        response.Value.Details.ContentType
        ?? "application/octet-stream";

    return Results.Stream(
        response.Value.Content,
        contentType);
})
.WithName("HamtaBildFil")
.WithSummary("Get the actual image — all roles");

/* 
Fotograf och Admin får ladda upp bilder
Skicka URL:en till bilden — lagra filen i Azure Blob Storage och använd den URL:en här 
*/

app.MapPost("/bilder", async (
    NyBild ny,
    HttpRequest req) =>
{
    // Check application role
    if (!HarBehorighet(
            HamtaRoll(req),
            "Fotograf"))
    {
        return Results.StatusCode(403);
    }

    if (string.IsNullOrWhiteSpace(ny.Namn))
        return Results.BadRequest("Bildnamn saknas.");

    if (string.IsNullOrWhiteSpace(ny.Url))
        return Results.BadRequest("Bild-URL saknas.");

    byte[] imageBytes;

    try
    {
        using var httpClient = new HttpClient();

        imageBytes =
            await httpClient.GetByteArrayAsync(ny.Url);
    }
    catch
    {
        return Results.BadRequest(
            "Kunde inte hämta bilden från URL:en.");
    }

    // Generate a new ID
    var id = await GetNextId();

    // Keep the original file extension
    var extension =
        Path.GetExtension(ny.Namn);

    if (string.IsNullOrWhiteSpace(extension))
    {
        extension = ".jpg";
    }

    // Example: 1.jpg
    var blobName = $"{id}{extension}";

    var blobClient =
        containerClient.GetBlobClient(blobName);

    using var imageStream =
        new MemoryStream(imageBytes);

    // Upload image to Azure Blob Storage
    await blobClient.UploadAsync(
        imageStream,
        new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType =
                    GetContentType(ny.Namn)
            },
            Metadata = new Dictionary<string, string>
            {
                ["originalName"] = ny.Namn,
                ["caption"] = ny.Caption,
                ["tags"] =
                    JsonSerializer.Serialize(
                        ny.Taggar ?? [])
            }
        });

    var bild = new Bild(
        id,
        ny.Namn,
        ny.Caption,
        ny.Taggar ?? [],
        $"/bilder/{id}/image");

    return Results.Created(
        $"/bilder/{id}",
        bild);
})
.WithName("LaddaUppBild")
.WithSummary("Add an image — requires Photographer or Admin");

// Fotograf och Admin får uppdatera caption och taggar
app.MapPut("/bilder/{id:int}", async (
    int id,
    BildUpdate update,
    HttpRequest req) =>
{
    if (!HarBehorighet(
            HamtaRoll(req),
            "Fotograf"))
    {
        return Results.StatusCode(403);
    }

    var blobClient =
        await FindImageBlob(id);

    if (blobClient is null)
        return Results.NotFound();

    var properties =
        await blobClient.GetPropertiesAsync();

    var metadata =
        properties.Value.Metadata;

    var caption =
        update.Caption
        ?? (
            metadata.TryGetValue(
                "caption",
                out var existingCaption)
                ? existingCaption
                : ""
        );

    List<string> tags;

    if (update.Taggar is not null)
    {
        tags = update.Taggar;
    }
    else if (
        metadata.TryGetValue(
            "tags",
            out var existingTags))
    {
        tags =
            JsonSerializer.Deserialize<List<string>>(
                existingTags) ?? [];
    }
    else
    {
        tags = [];
    }

    metadata["caption"] = caption;
    metadata["tags"] =
        JsonSerializer.Serialize(tags);

    await blobClient.SetMetadataAsync(metadata);

    var originalName =
        metadata.TryGetValue(
            "originalName",
            out var name)
            ? name
            : blobClient.Name;

    var bild = new Bild(
        id,
        originalName,
        caption,
        tags,
        $"/bilder/{id}/image");

    return Results.Ok(bild);
})
.WithName("UppdateraBild")
.WithSummary(
    "Update image — requires Photographer or Admin");


// Bara Admin får ta bort bilder — testa med Postman som Betraktare för att se 403
app.MapDelete("/bilder/{id:int}", async (
    int id,
    HttpRequest req) =>
{
    if (!HarBehorighet(
            HamtaRoll(req),
            "Admin"))
    {
        return Results.StatusCode(403);
    }

    var blobClient =
        await FindImageBlob(id);

    if (blobClient is null)
        return Results.NotFound();

    await blobClient.DeleteAsync();

    return Results.NoContent();
})
.WithName("RaderaBild")
.WithSummary(
    "Delete image — requires Admin");
app.Run();

/* 
======================================================
Rollkontroll
======================================================

Läser rollen ur Easy Auth-headern som Azure injicerar efter inloggning.
Lokalt (utan Easy Auth): returnerar "Admin" så Swagger fungerar utan inloggning. 
*/
string HamtaRoll(HttpRequest request)
{
    var header = request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();

    if (string.IsNullOrEmpty(header))
    {
        if (app.Environment.IsDevelopment())
        {
            return "Admin";
        }

        return "Betraktare";
    }

    try
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(header));
        using var doc = JsonDocument.Parse(json);

        var claims = doc.RootElement.GetProperty("claims").EnumerateArray();

        foreach (var claim in claims)
        {
            if (claim.GetProperty("typ").GetString() == "roles")
                return claim
                            .GetProperty("val")
                            .GetString() ?? "Betraktare";
        }
    }
    catch
    {
        return "Betraktare";
    }

    return "Betraktare"; // okänd roll → minsta behörighet
}

/*Kontrollerar om en roll har tillräcklig behörighet.
Hierarki: Betraktare < Fotograf < Admin
*/
bool HarBehorighet(string roll, string kravRoll) => (roll, kravRoll) switch
{
    (_, "Betraktare") => true,
    ("Fotograf" or "Admin", "Fotograf") => true,
    ("Admin", "Admin") => true,
    _ => false
};


// HJÄLP FUNKTIONER 




async Task<BlobClient?> FindImageBlob(int id)
{
    await foreach (
        var blob in containerClient.GetBlobsAsync())
    {
        var fileName =
            Path.GetFileNameWithoutExtension(
                blob.Name);

        if (fileName == id.ToString())
        {
            return containerClient.GetBlobClient(
                blob.Name);
        }
    }

    return null;
}

async Task<int> GetNextId()
{
    var maxId = 0;

    await foreach (var blob in containerClient.GetBlobsAsync(
        BlobTraits.None,
        BlobStates.None,
        null,
        default))
    {
        var fileName =
            Path.GetFileNameWithoutExtension(blob.Name);

        if (int.TryParse(fileName, out var id))
        {
            maxId = Math.Max(maxId, id);
        }
    }

    return maxId + 1;
}

Bild? CreateBildFromBlob(BlobItem blob)
{
    var fileName =
        Path.GetFileNameWithoutExtension(blob.Name);

    if (!int.TryParse(fileName, out var id))
    {
        return null;
    }

    var metadata = blob.Metadata;

    var originalName =
        metadata.TryGetValue("originalName", out var name)
            ? name
            : blob.Name;

    var caption =
        metadata.TryGetValue("caption", out var captionValue)
            ? captionValue
            : "";

    var tags =
        metadata.TryGetValue("tags", out var tagsValue)
            ? JsonSerializer.Deserialize<List<string>>(tagsValue) ?? []
            : [];

    return new Bild(
        id,
        originalName,
        caption,
        tags,
        $"/bilder/{id}/image");
}



string GetContentType(string fileName)
{
    return Path.GetExtension(
        fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };
}

/* 
======================================================
Datamodeller
======================================================
*/
record Bild(int Id, string Namn, string Caption, List<string> Taggar, string Url);

record NyBild(string Namn, string Caption, List<string>? Taggar, string Url);
record BildUpdate(string? Caption, List<string>? Taggar);



