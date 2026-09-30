namespace FypWeb.Helpers
{
    /// <summary>
    /// File upload helper ported from AccountCreationController.SaveImage.
    /// Writes to wwwroot/Images and returns the DB-compatible relative path
    /// ("/Images/xxx.jpg"), matching exactly what the API stored.
    /// </summary>
    public class ImageService
    {
        private readonly IWebHostEnvironment _env;

        public ImageService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task<string?> SaveImageAsync(IFormFile? file, string identifier)
        {
            if (file == null || file.Length == 0) return null;

            List<string> allowedExt = new() { ".jpg", ".jpeg", ".png" };
            string rawExt = !string.IsNullOrEmpty(file.FileName) ? Path.GetExtension(file.FileName) : ".jpg";
            string ext = string.IsNullOrEmpty(rawExt) ? ".jpg" : rawExt.ToLower();

            if (!allowedExt.Contains(ext)) return "Invalid";

            string rootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            string folder = Path.Combine(rootPath, "Images");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string safeIdentifier = string.Concat((identifier ?? "file").Split(Path.GetInvalidFileNameChars()))
                .Replace("@", "_").Replace(".", "_");
            string myfn = safeIdentifier + ext;
            string filePath = Path.Combine(folder, myfn);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return "/Images/" + myfn;
        }
    }
}
