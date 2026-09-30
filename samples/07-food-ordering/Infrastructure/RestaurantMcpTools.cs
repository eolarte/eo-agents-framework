namespace food_ordering.Infrastructure;

public static class RestaurantMcpTools
{
    public static string GetRestaurantInformation(string question)
        => "Fictional Green Fork restaurant information: open daily 11:00-22:00; delivery zones are Central, Riverside, and North Park; " +
           "illustrative delivery estimates are 25-40 minutes in Central, 35-50 minutes in Riverside, and 40-55 minutes in North Park. " +
           "These are static demonstration values, not live restaurant or courier data. Customer question: " + question;
}
