using System.Text;
using System.Text.Json;

public static class RoleMapping
{
    public static string GetRole(HttpRequest request, bool isDevEnv)
    {
        var header = request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();

        if (string.IsNullOrEmpty(header))
        {
            if (isDevEnv)
                return "Admin";

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
    public static bool HasPermission(string role, string claimRole) => (role, claimRole) switch
    {
        (_, "Betraktare") => true,
        ("Fotograf" or "Admin", "Fotograf") => true,
        ("Admin", "Admin") => true,
        _ => false
    };
}
