namespace food_ordering.Domain;

public sealed class RestaurantData
{
    public IReadOnlyList<MenuItem> Menu { get; } =
    [
        new("margherita-pizza", "Margherita Pizza", "Main", "Tomato, mozzarella, basil, and olive oil.", 14.50m, ["vegetarian", "pizza"]),
        new("spicy-chicken-burger", "Spicy Chicken Burger", "Main", "Crispy chicken, lettuce, pickles, and spicy sauce.", 13.00m, ["spicy", "chicken", "burger"]),
        new("falafel-grain-bowl", "Falafel Grain Bowl", "Main", "Falafel, grains, hummus, cucumber, tomato, and tahini.", 12.50m, ["vegan", "vegetarian", "healthy"]),
        new("grilled-salmon-bowl", "Grilled Salmon Bowl", "Main", "Grilled salmon, rice, greens, avocado, and lemon dressing.", 18.00m, ["pescatarian", "healthy", "fish"]),
        new("chicken-caesar-salad", "Chicken Caesar Salad", "Main", "Romaine lettuce, grilled chicken, parmesan, and Caesar dressing.", 11.50m, ["chicken", "salad"]),
        new("truffle-fries", "Truffle Fries", "Side", "Crispy fries with truffle oil and parmesan.", 6.50m, ["vegetarian", "side"]),
        new("chocolate-brownie", "Chocolate Brownie", "Dessert", "Warm chocolate brownie with a soft center.", 5.00m, ["vegetarian", "dessert", "sweet"]),
        new("sparkling-water", "Sparkling Water", "Drink", "Chilled sparkling mineral water.", 3.00m, ["vegan", "drink"])
    ];

    [System.ComponentModel.Description("Searches the restaurant menu by dish name, category, ingredient, or dietary tag.")]
    public IReadOnlyList<MenuItem> SearchMenu(
        [System.ComponentModel.Description("A food preference, ingredient, category, or dietary requirement to search for.")] string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Menu.Where(item => terms.Length == 0 || terms.Any(term =>
            item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            item.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            item.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            item.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .Take(8)
            .ToArray();
    }
}

public sealed record MenuItem(string Id, string Name, string Category, string Description, decimal Price, IReadOnlyList<string> Tags);
