using Fyp_Backend.Models;

namespace FypWeb.Services
{
    public static class HabitSelectionRules
    {
        // A worker or client can choose at most one option from each
        // explicitly configured exclusive group. A null result means valid.
        public static string? FindExclusiveGroupConflict(IEnumerable<Habit> habits)
        {
            return habits
                .Where(h => !string.IsNullOrWhiteSpace(h.ExclusiveGroup))
                .GroupBy(h => h.ExclusiveGroup!.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)
                ?.Key;
        }
    }
}
