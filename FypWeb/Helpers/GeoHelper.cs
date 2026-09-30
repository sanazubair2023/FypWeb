using Fyp_Backend.Models;

namespace FypWeb.Helpers
{
    /// <summary>
    /// Geo utilities ported verbatim from the original API's DashboardController.
    /// The core business rule is unchanged:
    ///   - Client inside the worker's radius  => "Part-Time" (must pick a time slot)
    ///   - Client outside it / missing coords => "Full-Time" (live-in / residence)
    /// </summary>
    public static class GeoHelper
    {
        private static double ToRadians(double val) => (Math.PI / 180.0) * val;

        public static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0; // Earth radius in kilometers
            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        public static string ClassifyJobType(Client? client, Worker? worker)
        {
            if (client == null || worker == null ||
                !client.Latitude.HasValue || !client.Longitude.HasValue ||
                !worker.Latitude.HasValue || !worker.Longitude.HasValue)
            {
                return "Full-Time";
            }

            double distanceKm = CalculateHaversineDistance(
                Convert.ToDouble(client.Latitude.Value), Convert.ToDouble(client.Longitude.Value),
                Convert.ToDouble(worker.Latitude.Value), Convert.ToDouble(worker.Longitude.Value));

            double workerRadius = worker.Radius > 0 ? worker.Radius : 5.0;

            return distanceKm <= workerRadius ? "Part-Time" : "Full-Time";
        }
    }
}
