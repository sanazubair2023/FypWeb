using System.Security.Claims;

namespace FypWeb.Helpers
{
    /// <summary>
    /// Convenience accessors for the signed-in user's claims.
    /// NameIdentifier holds the role-specific id (ClientId / WorkerId / CompanyID / PoliceID),
    /// exactly like the "sub" claim the original JWT carried.
    /// </summary>
    public static class ClaimsExtensions
    {
        public static int GetId(this ClaimsPrincipal user)
        {
            var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(idStr, out var id) ? id : 0;
        }

        public static string GetRole(this ClaimsPrincipal user)
            => user.FindFirstValue(ClaimTypes.Role) ?? "";

        public static string GetDisplayName(this ClaimsPrincipal user)
            => user.FindFirstValue(ClaimTypes.Name) ?? "User";

        public static string GetPicture(this ClaimsPrincipal user)
            => user.FindFirstValue("Picture") ?? "";

        public static string GetEmail(this ClaimsPrincipal user)
            => user.FindFirstValue("Email") ?? "";
    }
}
