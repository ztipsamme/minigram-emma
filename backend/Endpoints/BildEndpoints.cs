using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using backend.Services;

public static class ImageEndpoints
{
    public static void MapImageEndpoints(
        this WebApplication app,
        bool isDev,
        string? devTestRole = "")
    {
        // Alla roller får se bilder
        app.MapGet("/bilder", async (ImageService imageService) =>
        {
            if (isDev)
                return Results.Ok(MockImages.Images);

            try
            {
                var images = await imageService.GetAllAsync();
                return Results.Ok(images);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.ToString(), statusCode: 500);
            }
        })
        .WithName("HamtaBilder")
        .WithSummary("Hämta alla bilder — alla roller");


        app.MapGet("/bilder/{id}", async (string id, ImageService imageService) =>
        {
            if (isDev)
            {
                var b = MockImages.Images.FirstOrDefault(b => b.Id == id);
                return b is not null ? Results.Ok(b) : Results.NotFound();
            }

            ImageDTO? image = await imageService.GetByIdAsync(id);

            return image is not null
                ? Results.Ok(image)
                : Results.NotFound();
        })
        .WithName("HamtaBild")
        .WithSummary("Hämta en specifik bild — alla roller");


        /* Fotograf och Admin får ladda upp bilder
        Skicka URL:en till bilden — lagra filen i Azure Blob Storage och använd den URL:en här */
        app.MapPost("/bilder", async (HttpRequest req, ImageService imageService) =>
        {
            var role = RoleMapping.GetRole(req, isDev, devTestRole ?? "");
 
            // HasPermission(role, "Fotograf") är redan sant för Admin.
            if (!RoleMapping.HasPermission(role, "Fotograf"))
                return Results.StatusCode(403);
 
            if (!req.HasFormContentType)
                return Results.BadRequest("Förväntar multipart/form-data.");
 
            var form = await req.ReadFormAsync();
            var file = form.Files["fil"] ?? form.Files.FirstOrDefault();
 
            if (file is null || file.Length == 0)
                return Results.BadRequest("Ingen fil bifogad.");
 
            var caption = form["caption"].FirstOrDefault() ?? string.Empty;
            var tags = (form["taggar"].FirstOrDefault() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
 
            if (isDev)
            {
                var mock = new Image(
                    Guid.NewGuid().ToString(),
                    file.FileName,
                    caption,
                    tags,
                    "https://placehold.co/400x300?text=MinGram");
 
                MockImages.Images.Add(mock);
                return Results.Created($"/bilder/{mock.Id}", mock);
            }
 
            try
            {
                await using var stream = file.OpenReadStream();
 
                var image = await imageService.CreateImageAsync(
                    file.FileName,
                    stream,
                    file.ContentType,
                    caption,
                    tags);
 
                return Results.Created($"/bilder/{image.Id}", image);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .DisableAntiforgery()
        .WithName("LaddaUppBild")
        .WithSummary("Lägg till bild — kräver Fotograf eller Admin");


        // Fotograf och Admin får uppdatera caption och taggar
        app.MapPut("/bilder/{id}", async (
            string id,
            ImageUpdate imageUpdate,
            HttpRequest req,
            ImageService imageService) =>
        {
            var role = RoleMapping.GetRole(req, isDev, devTestRole ?? "");
 
            if (!RoleMapping.HasPermission(role, "Fotograf"))
                return Results.StatusCode(403);
 
            if (isDev)
            {
                var index = MockImages.Images.FindIndex(b => b.Id == id);
                if (index < 0) return Results.NotFound();
 
                var current = MockImages.Images[index];
 
                var updatedMock = current with
                {
                    Caption = !string.IsNullOrWhiteSpace(imageUpdate.Caption)
                        ? imageUpdate.Caption
                        : current.Caption,
 
                    Tags = imageUpdate.Tags is { Count: > 0 }
                        ? imageUpdate.Tags
                        : current.Tags
                };
 
                MockImages.Images[index] = updatedMock;
                return Results.Ok(updatedMock);
            }
 
            var image = await imageService.UpdateImageAsync(id, imageUpdate);
 
            return image is not null
                ? Results.Ok(image)
                : Results.NotFound();
        })
        .WithName("UppdateraBild")
        .WithSummary("Uppdatera bild — kräver Fotograf eller Admin");

        /* Bara Admin får ta bort bilder — testa med Postman som Betraktare för att se 403 */
        app.MapDelete("/bilder/{id}", async (
            string id,
            HttpRequest req,
            ImageService imageService) =>
        {
            var role = RoleMapping.GetRole(req, isDev, devTestRole ?? "");
 
            if (!RoleMapping.HasPermission(role, "Admin"))
                return Results.StatusCode(403);
 
            if (isDev)
            {
                var mock = MockImages.Images.FirstOrDefault(b => b.Id == id);
                if (mock is null) return Results.NotFound();
 
                MockImages.Images.Remove(mock);
                return Results.NoContent();
            }
 
            var deleted = await imageService.DeleteImageByIdAsync(id);
 
            return deleted
                ? Results.NoContent()
                : Results.NotFound();
        })
        .WithName("RaderaBild")
        .WithSummary("Radera bild — kräver Admin");
    }
}
