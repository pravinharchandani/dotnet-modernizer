using System;
using System.Globalization;
using System.Linq;

namespace LegacyShop.Utils
{
    public static class StringHelpers
    {
        public static string ToTitleCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
        }

        public static string Truncate(string value, int maxLength, string suffix = "…")
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            if (maxLength < 0)
            {
                throw new ArgumentOutOfRangeException("maxLength");
            }

            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + suffix;
        }

        public static string ToSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = value
                .Trim()
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '-');

            string slug = new string(chars.ToArray());
            while (slug.Contains("--"))
            {
                slug = slug.Replace("--", "-");
            }

            return slug.Trim('-');
        }
    }
}
