// Program.cs — MinGram API
// ASP.NET Core Minimal API: endpoints definieras direkt här, inga controllers.
//
// Starta lokalt:  dotnet run
// Swagger UI:     https://localhost:{port}/swagger
//
// v35 — Azure-konfiguration (görs i portalen, inte i koden):
// 1. CORS: App Service → API → CORS → lägg till din frontend-URL
// 2. Easy Auth: App Service → Authentication → Add identity provider → Microsoft
//    Välj din Entra ID-tenant. Alla anrop kräver nu inloggning.
// 3. App-roller i Entra ID: gå till App registrations → din app → App roles
//    Skapa rollerna Betraktare, Fotograf, Admin.
//    Tilldela dem till dina Entra ID-användare under Enterprise applications.
//
// Bilder lagras som URL:er — ladda upp till Azure Blob Storage och skicka URL:en hit.

using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS — hanteras primärt i Azure Portal: App Service → API → CORS
// Lägg till din frontend-URL där, så slipper du ändra och redeploya koden.
// Den här koden hanterar CORS lokalt under utveckling.
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

// -------------------------------------------------------
// In-memory datastore med seed-data
// Datan nollställs vid omstart — en riktig app lagrar bilder i Blob Storage
// -------------------------------------------------------

var bilder = new List<Bild>
{
    new(1, "demo.jpg", "Demobild — ersätt med din egen", ["demo", "placeholder"],
        "https://placehold.co/400x300?text=MinGram")
};
var nastaBildId = 2;

// ======================================================
// Bilder
// ======================================================

// Alla roller får se bilder
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

// Fotograf och Admin får ladda upp bilder
// Skicka URL:en till bilden — lagra filen i Azure Blob Storage och använd den URL:en här
app.MapPost("/bilder", (NyBild ny, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf")) return Results.StatusCode(403);
    var b = new Bild(nastaBildId++, ny.Namn, ny.Caption, ny.Taggar ?? [], ny.Url);
    bilder.Add(b);
    return Results.Created($"/bilder/{b.Id}", b);
})
.WithName("LaddaUppBild")
.WithSummary("Lägg till bild — kräver Fotograf eller Admin");

// Fotograf och Admin får uppdatera caption och taggar
app.MapPut("/bilder/{id:int}", (int id, BildUpdate update, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf")) return Results.StatusCode(403);
    var index = bilder.FindIndex(b => b.Id == id);
    if (index < 0) return Results.NotFound();
    bilder[index] = bilder[index] with
    {
        Caption = update.Caption ?? bilder[index].Caption,
        Taggar  = update.Taggar  ?? bilder[index].Taggar
    };
    return Results.Ok(bilder[index]);
})
.WithName("UppdateraBild")
.WithSummary("Uppdatera bild — kräver Fotograf eller Admin");

// Bara Admin får ta bort bilder — testa med Postman som Betraktare för att se 403
app.MapDelete("/bilder/{id:int}", (int id, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Admin")) return Results.StatusCode(403);
    var b = bilder.FirstOrDefault(b => b.Id == id);
    if (b is null) return Results.NotFound();
    bilder.Remove(b);
    return Results.NoContent();
})
.WithName("RaderaBild")
.WithSummary("Radera bild — kräver Admin");

app.Run();

// ======================================================
// Rollkontroll
// ======================================================

// Läser rollen ur Easy Auth-headern som Azure injicerar efter inloggning.
// Lokalt (utan Easy Auth): returnerar "Admin" så Swagger fungerar utan inloggning.
string HamtaRoll(HttpRequest request)
{
    var header = request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();
    if (string.IsNullOrEmpty(header)) return "Admin"; // lokal dev

    try
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(header));
        using var doc = JsonDocument.Parse(json);
        foreach (var claim in doc.RootElement.GetProperty("claims").EnumerateArray())
        {
            if (claim.GetProperty("typ").GetString() == "roles")
                return claim.GetProperty("val").GetString() ?? "Betraktare";
        }
    }
    catch { }

    return "Betraktare"; // okänd roll → minsta behörighet
}

// Kontrollerar om en roll har tillräcklig behörighet.
// Hierarki: Betraktare < Fotograf < Admin
bool HarBehorighet(string roll, string kravRoll) => (roll, kravRoll) switch
{
    (_, "Betraktare")          => true,
    ("Fotograf" or "Admin", "Fotograf") => true,
    ("Admin", "Admin")         => true,
    _                          => false
};

// ======================================================
// Datamodeller
// ======================================================

record Bild(int Id, string Namn, string Caption, List<string> Taggar, string Url);

record NyBild(string Namn, string Caption, List<string>? Taggar, string Url);
record BildUpdate(string? Caption, List<string>? Taggar);
