record Bild(int Id, string Namn, string Caption, List<string> Taggar, string Url);
record NyBild(string Namn, string Caption, List<string>? Taggar, string Url);
record BildUpdate(string? Caption, List<string>? Taggar);
