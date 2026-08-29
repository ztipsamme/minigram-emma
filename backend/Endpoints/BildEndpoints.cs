using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using backend.Services;

public static class ImageEndpoints
{
    public static void MapImageEndpoints(
        this WebApplication app,
        bool isDev)
    {
        var nextImage = 2;

        app.MapPost("/bilder", async (IFormFile file, string caption, string[]? tags, ImageService imageService, HttpRequest req) =>
        {
            var role = RoleMapping.GetRole(req, isDev);
            if (!RoleMapping.HasPermission(role, "Fotograf") && !RoleMapping.HasPermission(role, "Admin")) return Results.StatusCode(403);

            var blobName = $"{Guid.NewGuid()}-{file.FileName}";
            await using var stream = file.OpenReadStream();
            await imageService.UploadAsync(blobName, stream, file.ContentType);

            var b = new Image(nextImage++, file.FileName, caption, tags != null ? tags.ToList() : [], blobName);

            MockImages.Images.Add(b);

            return Results.Created($"/bilder/{b.Id}", b);
        })
        // .DisableAntiforgery() // om du testar via Swagger/Postman med form-data
        .WithName("LaddaUppBild")
        .WithSummary("Lägg till bild — kräver Fotograf eller Admin");

        app.MapGet("/bilder/{id:int}/bild", async (int id, ImageService imageService, HttpRequest req) =>
        {
            if (!RoleMapping.HasPermission(RoleMapping.GetRole(req, isDev), "Betraktare"))
                return Results.StatusCode(403);

            var b = MockImages.Images.FirstOrDefault(x => x.Id == id);
            if (b is null) return Results.NotFound();

            var result = await imageService.DownloadAsync(b.Url); // Url-fältet innehåller nu blob-namnet
            if (result is null) return Results.NotFound();

            return Results.Stream(result.Value.Content, result.Value.ContentType);
        })
        .WithName("HamtaBildInnehall");


        // Fotograf och Admin får uppdatera caption och taggar
        app.MapPut("/bilder/{id:int}", (int id, ImageUpdate update, HttpRequest req) =>
        {
            var role = RoleMapping.GetRole(req, isDev);
            if (!RoleMapping.HasPermission(role, "Fotograf") && !RoleMapping.HasPermission(role, "Admin")) return Results.StatusCode(403);

            var index = MockImages.Images.FindIndex(b => b.Id == id);
            if (index < 0) return Results.NotFound();
            MockImages.Images[index] = MockImages.Images[index] with
            {
                Caption = update.Caption ?? MockImages.Images[index].Caption,
                Tags = update.Tags ?? MockImages.Images[index].Tags
            };
            return Results.Ok(MockImages.Images[index]);
        })
        .WithName("UppdateraBild")
        .WithSummary("Uppdatera bild — kräver Fotograf eller Admin");


        // Bara Admin får ta bort bilder — testa med Postman som Betraktare för att se 403
        app.MapDelete("/bilder/{id:int}", async (int id, ImageService imageService, HttpRequest req) =>
        {
            if (!RoleMapping.HasPermission(RoleMapping.GetRole(req, isDev), "Admin"))
                return Results.StatusCode(403);

            var b = MockImages.Images.FirstOrDefault(x => x.Id == id);
            if (b is null) return Results.NotFound();

            await imageService.DeleteAsync(b.Url);
            MockImages.Images.Remove(b);
            return Results.NoContent();
        })
        .WithName("RaderaBild");
    }
}
