using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The runtime recipe list as RecipeDatabase builds it: RecipeSO assets written by GameDataImporter into
    /// Resources/RecipeDatabase.asset, converted to RecipeJson. If this fails, re-import via
    /// 'MobileIdleBuilder > Import Game Data'.
    /// </summary>
    internal static class RecipeTestData
    {
        public static List<RecipeJson> LoadRuntimeRecipes()
        {
            var db = Resources.Load<RecipeDatabaseSO>("RecipeDatabase");
            Assert.IsNotNull(db, "Resources/RecipeDatabase.asset not found - re-import game data");
            Assert.IsNotNull(db.allRecipes, "RecipeDatabase.allRecipes is null");
            var recipes = RecipeDatabase.ConvertAll(db.allRecipes);
            Assert.IsNotEmpty(recipes, "No valid recipes in RecipeDatabase.asset");
            return recipes;
        }
    }
}
