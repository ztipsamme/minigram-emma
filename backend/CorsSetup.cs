public static class CorsSetup
{
    public static void ConfigureCors(this WebApplicationBuilder builder)
    {
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
    }
}
