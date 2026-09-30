using System.ComponentModel;

sealed record MenuItem(
    string Id,
    string Name,
    string Category,
    string Description,
    decimal Price,
    IReadOnlyList<string> Tags);

sealed class RestaurantMenu
{
    private static readonly IReadOnlyList<MenuItem> Items =
    [
        new(
            "pizza-margherita",
            "Margherita Pizza",
            "Main",
            "Tomato, mozzarella, basil, and olive oil.",
            14.50m,
            ["vegetarian", "pizza", "italian"]),
        new(
            "chicken-burger",
            "Spicy Chicken Burger",
            "Main",
            "Crispy chicken, lettuce, pickles, and spicy sauce.",
            13.00m,
            ["spicy", "chicken", "burger", "american"]),
        new(
            "falafel-bowl",
            "Falafel Grain Bowl",
            "Main",
            "Falafel, grains, hummus, cucumber, tomato, and tahini.",
            12.50m,
            ["vegan", "vegetarian", "healthy", "middle-eastern"]),
        new(
            "salmon-bowl",
            "Grilled Salmon Bowl",
            "Main",
            "Grilled salmon, rice, greens, avocado, and lemon dressing.",
            18.00m,
            ["pescatarian", "healthy", "fish", "japanese"]),
        new(
            "caesar-salad",
            "Chicken Caesar Salad",
            "Main",
            "Romaine lettuce, grilled chicken, parmesan, and Caesar dressing.",
            11.50m,
            ["chicken", "salad", "italian"]),
        new(
            "truffle-fries",
            "Truffle Fries",
            "Side",
            "Crispy fries with truffle oil and parmesan.",
            6.50m,
            ["vegetarian", "side", "french"]),
        new(
            "chocolate-brownie",
            "Chocolate Brownie",
            "Dessert",
            "Warm chocolate brownie with a soft center.",
            5.00m,
            ["vegetarian", "dessert", "sweet", "american"]),
        new(
            "sparkling-water",
            "Sparkling Water",
            "Drink",
            "Chilled sparkling mineral water.",
            3.00m,
            ["vegan", "drink"])
    ];

    [Description("Searches the restaurant menu by dish name, category, ingredient, cuisine, or dietary tag.")]
    public IReadOnlyList<MenuItem> Search(
        [Description("A food preference, ingredient, category, cuisine, or dietary requirement to search for.")]
        string query)
    {
        var terms = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Items
            .Where(item =>
                terms.Length == 0 ||
                terms.Any(term =>
                    item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .Take(8)
            .ToArray();
    }

    public MenuItem? FindByName(string name)
        => Items.FirstOrDefault(item =>
            item.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
}
