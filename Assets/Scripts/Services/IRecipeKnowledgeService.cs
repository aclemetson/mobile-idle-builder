namespace MobileIdleBuilder
{
    public interface IRecipeKnowledgeService
    {
        bool IsKnown(string recipeId);
        void MarkKnown(string recipeId);
    }
}
