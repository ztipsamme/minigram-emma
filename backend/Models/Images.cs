public record Bild(int Id, string Namn, string Caption, List<string> Taggar, string Url);
public record NyBild(string Namn, string Caption, List<string>? Taggar, string Url);
public record BildUpdate(string? Caption, List<string>? Taggar);
