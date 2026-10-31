namespace FypWeb.Helpers;

/// <summary>Validated, randomly named image uploads. Identifiers are never used as storage keys.</summary>
public sealed class ImageService(IWebHostEnvironment env)
{
    private const long MaxBytes = 5 * 1024 * 1024;

    public async Task<string?> SaveImageAsync(IFormFile? file, string? ignoredLegacyIdentifier = null)
    {
        if (file == null || file.Length == 0) return null;
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png") || file.Length > MaxBytes) return "Invalid";

        var root = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var folder = Path.Combine(root, "Images");
        Directory.CreateDirectory(folder);
        var name = Guid.NewGuid().ToString("N") + ext;
        var finalPath = Path.Combine(folder, name);
        var tempPath = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".upload");
        try
        {
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output);
            }
            if (new FileInfo(tempPath).Length > MaxBytes || !await HasValidSignature(tempPath, ext))
            {
                System.IO.File.Delete(tempPath);
                return "Invalid";
            }
            System.IO.File.Move(tempPath, finalPath, false);
            return "/Images/" + name;
        }
        catch
        {
            if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            throw;
        }
    }

    private static async Task<bool> HasValidSignature(string path, string ext)
    {
        var header = new byte[12];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var read = await stream.ReadAsync(header);
        if (ext == ".png")
            return read >= 8 && header.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        return read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
    }
}
