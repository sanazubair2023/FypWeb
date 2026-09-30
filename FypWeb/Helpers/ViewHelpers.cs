namespace FypWeb.Helpers
{
    /// <summary>Small helpers used inside Razor views.</summary>
    public static class ViewHelpers
    {
        /// <summary>
        /// Normalises any stored picture value into a servable URL.
        /// DB values may be "/Images/x.jpg", "x.jpg", "Images/x.jpg" or an http(s) URL.
        /// Returns null when there is no usable picture (view shows initials fallback).
        /// </summary>
        public static string? Pic(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            if (raw.StartsWith("http://") || raw.StartsWith("https://")) return raw;
            if (raw.StartsWith("/Images/")) return raw;
            if (raw.StartsWith("Images/")) return "/" + raw;
            return "/Images/" + raw.TrimStart('/');
        }

        /// <summary>First letter of a name, upper-cased, for avatar fallbacks.</summary>
        public static string Initial(string? name)
            => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[0].ToString().ToUpper();
    }
}
