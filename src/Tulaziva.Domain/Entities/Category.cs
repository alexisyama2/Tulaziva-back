namespace Tulaziva.Domain.Entities;

public class Category
{
    public int Id { get; set; }

    /// <summary>Stable code of the category (for example "vehicles"). The name shown to the user is translated from it.</summary>
    public string Key { get; set; } = string.Empty;
}

/// <summary>The fixed list of categories. It is saved by the migrations, so a change here needs a new migration.</summary>
public static class DefaultCategories
{
    public static readonly IReadOnlyList<Category> All = new[]
    {
        new Category { Id = 1, Key = "vehicles" },
        new Category { Id = 2, Key = "realEstate" },
        new Category { Id = 3, Key = "jobs" },
        new Category { Id = 4, Key = "services" },
        new Category { Id = 5, Key = "electronics" },
        new Category { Id = 6, Key = "home" },
        new Category { Id = 7, Key = "fashion" },
        new Category { Id = 8, Key = "pets" },
        new Category { Id = 9, Key = "sports" },
        new Category { Id = 10, Key = "other" },
        new Category { Id = 11, Key = "personalServices" }
    };
}
