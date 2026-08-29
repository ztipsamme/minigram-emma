using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using backend.Services;

public static class ImageEndpoints
{
    public static void MapImageEndpoints(
        this WebApplication app,
        ImageService imageService,
        bool isDev)
    {
        app.MapGet("/bilder", async () =>
        {
            try
            {
                var images = await imageService.GetAllAsync();
                return Results.Ok(images);
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
            var image = await imageService.GetByIdAsync(id);
            return image is null ? Results.NotFound() : Results.Ok(image);
        })
        .WithName("HamtaBild")
        .WithSummary("Get a specific image — all roles");


        app.MapGet("/bilder/{id:int}/image", async (int id) =>
        {
            var blobClient = await imageService.FindImageBlobAsync(id);
            if (blobClient is null)
                return Results.NotFound();

            var response = await blobClient.DownloadStreamingAsync();
            var contentType = response.Value.Details.ContentType ?? "application/octet-stream";
            return Results.Stream(response.Value.Content, contentType);
        })
        .WithName("HamtaBildFil")
        .WithSummary("Get the actual image — all roles");


        app.MapPost("/bilder", async (
            NewImage newImage,
            HttpRequest req) =>
        {
            if (!RoleMapping.HasPermission(RoleMapping.GetRole(req, isDev), "Fotograf") &&
                !RoleMapping.HasPermission(RoleMapping.GetRole(req, isDev), "Admin"))
            {
                return Results.StatusCode(403);
            }

            if (string.IsNullOrWhiteSpace(newImage.Name))
                return Results.BadRequest("Bildnamn saknas.");

            if (string.IsNullOrWhiteSpace(newImage.Url))
                return Results.BadRequest("Bild-URL saknas.");

            try
            {
                var blobClient = await imageService.CreateAsync(newImage);
                var id = int.Parse(Path.GetFileNameWithoutExtension(blobClient.Name));
                var image = new Image(
                    id,
                    newImage.Name,
                    newImage.Caption,
                    newImage.Tags ?? [],
                    $"/bilder/{id}/image");

                return Results.Created($"/bilder/{id}", image);
            }
            catch
            {
                return Results.BadRequest("Kunde inte hämta bilden från URL:en.");
            }
        })
        .WithName("LaddaUppImage")
        .WithSummary("Add an image — requires Photographer or Admin");


        app.MapPut("/bilder/{id:int}", async (
            int id,
            ImageUpdate update,
            HttpRequest req) =>
        {
            if (!RoleMapping.HasPermission(RoleMapping.GetRole(req, isDev), "Fotograf") &&
                !RoleMapping.HasPermission(RoleMapping.GetRole(req, isDev), "Admin"))
            {
                return Results.StatusCode(403);
            }

            var blobClient = await imageService.FindImageBlobAsync(id);
            if (blobClient is null)
                return Results.NotFound();

            var properties = await blobClient.GetPropertiesAsync();
            var metadata = properties.Value.Metadata;

            var caption = update.Caption ??
                (metadata.TryGetValue("caption", out var existingCaption) ? existingCaption : "");

            List<string> tags;
            if (update.TaggTagsr is not null)
            {
                tags = update.TaggTagsr;
            }
            else if (metadata.TryGetValue("tags", out var existingTags))
            {
                tags = JsonSerializer.Deserialize<List<string>>(existingTags) ?? [];
            }
            else
            {
                tags = [];
            }

            metadata["caption"] = caption;
            metadata["tags"] = JsonSerializer.Serialize(tags);
            await blobClient.SetMetadataAsync(metadata);

            var originalName = metadata.TryGetValue("originalName", out var name)
                ? name
                : blobClient.Name;

            var image = new Image(id, originalName, caption, tags, $"/bilder/{id}/image");
            return Results.Ok(image);
        })
        .WithName("UppdateraBild")
        .WithSummary("Update image — requires Photographer or Admin");


        app.MapDelete("/bilder/{id:int}", async (
            int id,
            HttpRequest req) =>
        {
            if (!RoleMapping.HasPermission(RoleMapping.GetRole(req, isDev), "Admin"))
            {
                return Results.StatusCode(403);
            }

            var blobClient = await imageService.FindImageBlobAsync(id);
            if (blobClient is null)
                return Results.NotFound();

            await blobClient.DeleteAsync();
            return Results.NoContent();
        })
        .WithName("RaderaBild")
        .WithSummary("Delete image — requires Admin");
    }
}
