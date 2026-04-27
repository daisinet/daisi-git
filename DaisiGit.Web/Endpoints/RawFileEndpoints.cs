using DaisiGit.Core.Git;
using DaisiGit.Core.Enums;
using DaisiGit.Services;

namespace DaisiGit.Web.Endpoints;

/// <summary>
/// Maps GitHub-style raw-file URLs that return a repo file in its native form
/// (no UI, correct Content-Type, anonymous-allowed for public repos).
///
///   /{owner}/{slug}/raw/branch/{branch}/{**path}
///   /{owner}/{slug}/raw/tag/{tag}/{**path}
///   /{owner}/{slug}/raw/sha/{sha}/{**path}
/// </summary>
public static class RawFileEndpoints
{
    public static void MapRawFileEndpoints(this WebApplication app)
    {
        app.MapGet("/{owner}/{slug}/raw/branch/{branch}/{**path}",
            (HttpContext ctx, string owner, string slug, string branch, string path,
                RepositoryService r, BrowseService b, PermissionService p)
                => ServeAsync(ctx, owner, slug, $"refs/heads/{branch}", path, r, b, p));

        app.MapGet("/{owner}/{slug}/raw/tag/{tag}/{**path}",
            (HttpContext ctx, string owner, string slug, string tag, string path,
                RepositoryService r, BrowseService b, PermissionService p)
                => ServeAsync(ctx, owner, slug, $"refs/tags/{tag}", path, r, b, p));

        app.MapGet("/{owner}/{slug}/raw/sha/{sha}/{**path}",
            (HttpContext ctx, string owner, string slug, string sha, string path,
                RepositoryService r, BrowseService b, PermissionService p)
                => ServeAsync(ctx, owner, slug, sha, path, r, b, p));
    }

    private static async Task<IResult> ServeAsync(
        HttpContext ctx, string owner, string slug, string refOrSha, string path,
        RepositoryService repoService, BrowseService browseService, PermissionService permissionService)
    {
        var repo = await repoService.GetRepositoryBySlugAsync(owner, slug);
        if (repo == null) return Results.NotFound();

        // Permission check honors the same rules as the file browser: public repos serve
        // anonymously, internal/private require an authenticated reader.
        var viewerId = ctx.Items["userId"] as string;
        if (repo.Visibility != GitRepoVisibility.Public)
        {
            if (string.IsNullOrEmpty(viewerId)) return Results.Unauthorized();
            if (!await permissionService.CanReadAsync(viewerId, repo)) return Results.Forbid();
        }

        var commitSha = await browseService.ResolveRefAsync(repo.id, refOrSha);
        if (commitSha == null) return Results.NotFound();

        var entry = await browseService.GetTreeAtPathAsync(repo.id, commitSha, path ?? "");
        if (entry == null || !entry.IsFile || string.IsNullOrEmpty(entry.FileSha))
            return Results.NotFound();

        var blob = await browseService.GetFileContentAsync(repo.id, entry.FileSha);
        if (blob == null) return Results.NotFound();

        var contentType = blob.IsBinary ? GuessBinaryContentType(path) : GuessTextContentType(path);
        // Disable the Razor 404 fallback for nested unknown subdirs by setting cache hints.
        ctx.Response.Headers["Cache-Control"] = "private, max-age=60";
        return Results.File(blob.Data, contentType, fileDownloadName: null);
    }

    private static string GuessTextContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" => "text/markdown; charset=utf-8",
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".ts" or ".tsx" => "application/typescript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".yaml" or ".yml" => "application/yaml; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            ".csv" => "text/csv; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".cs" or ".razor" or ".cshtml" => "text/plain; charset=utf-8",
            ".py" or ".rb" or ".go" or ".rs" or ".java" or ".kt" => "text/plain; charset=utf-8",
            ".sh" or ".bash" or ".zsh" => "text/plain; charset=utf-8",
            _ => "text/plain; charset=utf-8"
        };
    }

    private static string GuessBinaryContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".bmp" => "image/bmp",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".gz" or ".tgz" => "application/gzip",
            ".tar" => "application/x-tar",
            ".7z" => "application/x-7z-compressed",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".wasm" => "application/wasm",
            _ => "application/octet-stream"
        };
    }
}
