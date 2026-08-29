using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

public static class BildEndpoints
{
    public static void MapBildEndpoints(
        this WebApplication app,
        BlobContainerClient containerClient,
        ImageStorageService imageStorageService,
        bool isDev)
    {
        app.MapGet("/bilder", async () =>
        {
            try
            {
                var bilder = new List<Bild>();

                await foreach (var blob in containerClient.GetBlobsAsync(
                    BlobTraits.Metadata,
                    BlobStates.None,
                    null,
                    default))
                {
                    var bild = imageStorageService.CreateBildFromBlob(blob);

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

        app.MapGet("/bilder/{id:int}", async (int id) =>
        {
            var blobClient = await imageStorageService.FindImageBlob(id);

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

        app.MapGet("/bilder/{id:int}/image", async (int id) =>
        {
            var blobClient = await imageStorageService.FindImageBlob(id);

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

        app.MapPost("/bilder", async (
            NyBild ny,
            HttpRequest req) =>
        {
            var roll = RoleMapping.HamtaRoll(req, isDev);
            if (!RoleMapping.HarBehorighet(roll, "Fotograf") && !RoleMapping.HarBehorighet(roll, "Admin"))
                return Results.StatusCode(403);

            if (string.IsNullOrWhiteSpace(ny.Namn))
                return Results.BadRequest("Bildnamn saknas.");

            if (string.IsNullOrWhiteSpace(ny.Url))
                return Results.BadRequest("Bild-URL saknas.");

            byte[] imageBytes;

            try
            {
                using var httpClient = new HttpClient();
                imageBytes = await httpClient.GetByteArrayAsync(ny.Url);
            }
            catch
            {
                return Results.BadRequest("Kunde inte hämta bilden från URL:en.");
            }

            var id = await imageStorageService.GetNextId();
            var extension = Path.GetExtension(ny.Namn);

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".jpg";
            }

            var blobName = $"{id}{extension}";
            var blobClient = containerClient.GetBlobClient(blobName);

            using var imageStream = new MemoryStream(imageBytes);

            await blobClient.UploadAsync(
                imageStream,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = ImageStorageService.GetContentType(ny.Namn)
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["originalName"] = ny.Namn,
                        ["caption"] = ny.Caption,
                        ["tags"] = JsonSerializer.Serialize(ny.Taggar ?? [])
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
            if (!RoleMapping.HarBehorighet(
                    RoleMapping.HamtaRoll(req, isDev),
                    "Fotograf"))
            {
                return Results.StatusCode(403);
            }

            var blobClient =
                await imageStorageService.FindImageBlob(id);

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

        app.MapDelete("/bilder/{id:int}", async (
            int id,
            HttpRequest req) =>
        {
            if (!RoleMapping.HarBehorighet(
                    RoleMapping.HamtaRoll(req, isDev),
                    "Admin"))
            {
                return Results.StatusCode(403);
            }

            var blobClient = await imageStorageService.FindImageBlob(id);

            if (blobClient is null)
                return Results.NotFound();

            await blobClient.DeleteAsync();

            return Results.NoContent();
        })
        .WithName("RaderaBild")
        .WithSummary("Delete image — requires Admin");
    }
}
