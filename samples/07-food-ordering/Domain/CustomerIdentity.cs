using System.Security.Cryptography;
using System.Text;

namespace food_ordering.Domain;

public static class CustomerIdentity
{
    public static string CreateStorageKey(string customerId)
    {
        var normalized = customerId.Trim().ToLowerInvariant();
        var slug = new string(normalized.Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length == 0)
        {
            throw new ArgumentException("Customer ID must contain at least one letter or digit.", nameof(customerId));
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..10].ToLowerInvariant();
        return $"{slug[..Math.Min(slug.Length, 48)]}-{hash}";
    }
}
