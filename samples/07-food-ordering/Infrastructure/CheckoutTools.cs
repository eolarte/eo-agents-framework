using System.ComponentModel;

namespace food_ordering.Infrastructure;

public static class CheckoutTools
{
    [Description("Checks whether a fictional payment choice is supported. Never send card details.")]
    public static string ValidateDemoPaymentMethod(
        [Description("Use only the labels 'demo-card' or 'cash'; never provide credentials or card numbers.")] string paymentMethod)
        => paymentMethod.Trim().ToLowerInvariant() switch
        {
            "demo-card" => "Demo card selected. No card details were collected and no payment was charged.",
            "cash" => "Cash selected for this local demonstration.",
            _ => "Unsupported demo payment choice. Ask the customer to choose demo-card or cash."
        };
}
