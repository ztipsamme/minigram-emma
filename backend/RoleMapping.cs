using System.Text;
using System.Text.Json;

public static class RoleMapping
{
    public static Dictionary<string, string> Load(IConfiguration configuration)
    {
        var json = configuration["RollMappningJson"];
        var mapping = string.IsNullOrEmpty(json)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, string>(mapping, StringComparer.OrdinalIgnoreCase);
    }

    public static string HamtaRoll(HttpRequest request, Dictionary<string, string> mapping, IHostEnvironment environment)
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
        if (email != null && mapping.TryGetValue(email, out var mapped))
            return mapped;

        if (string.IsNullOrEmpty(header) && environment.IsDevelopment())
            return "Admin";

        return "Betraktare";
    }

    public static string? HamtaEmail(HttpRequest request)
    {
        // Workaround: frontend skickar e-post efter egen Easy Auth-login
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
                    var value = claim.GetProperty("val").GetString();
                    if (!string.IsNullOrWhiteSpace(value) && value.Contains('@'))
                        return value;
                }
            }
        }
        catch { }

        return null;
    }

    public static bool HarBehorighet(string role, string requiredRole) => (role, requiredRole) switch
    {
        (_, "Betraktare") => true,
        ("Fotograf" or "Admin", "Fotograf") => true,
        ("Admin", "Admin") => true,
        _ => false
    };
}
