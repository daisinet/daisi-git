namespace DaisiGit.Web.Endpoints;

/// <summary>
/// Serves CLI install scripts and binary downloads.
/// Binaries are stored in wwwroot/cli/ after a release build.
/// </summary>
public static class CliEndpoints
{
    public static void MapCliEndpoints(this WebApplication app)
    {
        // Install scripts
        app.MapGet("/cli/install.sh", async ctx =>
        {
            var path = Path.Combine(app.Environment.WebRootPath, "cli", "install.sh");
            if (!File.Exists(path))
            {
                ctx.Response.StatusCode = 404;
                return;
            }
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.SendFileAsync(path);
        });

        app.MapGet("/cli/install.ps1", async ctx =>
        {
            var path = Path.Combine(app.Environment.WebRootPath, "cli", "install.ps1");
            if (!File.Exists(path))
            {
                ctx.Response.StatusCode = 404;
                return;
            }
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.SendFileAsync(path);
        });

        // Binary downloads
        app.MapGet("/cli/download/{filename}", async (HttpContext ctx, string filename) =>
        {
            // Sanitize filename — only allow expected binary names
            var allowed = new HashSet<string>
            {
                "dg-win-x64.exe",
                "dg-linux-x64",
                "dg-osx-x64",
                "dg-osx-arm64"
            };

            if (!allowed.Contains(filename))
            {
                ctx.Response.StatusCode = 404;
                return;
            }

            var path = Path.Combine(app.Environment.WebRootPath, "cli", filename);
            if (!File.Exists(path))
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync(
                    "Binary not found. The CLI may not have been published yet.");
                return;
            }

            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            await ctx.Response.SendFileAsync(path);
        });

        // Version check — returns the latest CLI version
        app.MapGet("/cli/version", async ctx =>
        {
            var versionFile = Path.Combine(app.Environment.WebRootPath, "cli", "version.txt");
            var version = File.Exists(versionFile)
                ? (await File.ReadAllTextAsync(versionFile)).Trim()
                : "0.1.0";
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync(version);
        });
    }
}
