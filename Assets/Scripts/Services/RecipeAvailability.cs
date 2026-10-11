namespace MobileIdleBuilder
{
    /// <summary>
    /// Which of a building's recipes the player can currently use (research-gated).
    /// Shared by the inspector's recipe picker and placement, so a building with exactly one
    /// usable recipe is set up automatically instead of sitting at "No recipe".
    /// </summary>
    public static class RecipeAvailability
    {
        public static bool IsAvailable(RecipeSO r)
            => IsAvailable(r, id => ResearchService.Instance != null && ResearchService.Instance.IsUnlocked(id));

        public static bool IsAvailable(RecipeSO r, System.Func<string, bool> isResearchUnlocked)
        {
            if (r == null) return false;
            if (r.knownFromStart || r.requiredResearch == null) return true;
            return isResearchUnlocked != null && isResearchUnlocked(r.requiredResearch.id);
        }

        /// <summary>The building's only available recipe, or null when there are zero or several.</summary>
        public static RecipeSO SoleAvailable(BuildingSO building)
            => SoleAvailable(building, id => ResearchService.Instance != null && ResearchService.Instance.IsUnlocked(id));

        public static RecipeSO SoleAvailable(BuildingSO building, System.Func<string, bool> isResearchUnlocked)
        {
            if (building?.supportedRecipes == null) return null;
            RecipeSO found = null;
            foreach (var r in building.supportedRecipes)
            {
                if (!IsAvailable(r, isResearchUnlocked)) continue;
                if (found != null) return null;
                found = r;
            }
            return found;
        }
    }
}
