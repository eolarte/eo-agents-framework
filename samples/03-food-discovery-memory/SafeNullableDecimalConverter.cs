using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

sealed class SafeNullableDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetDecimal(out var decimalValue))
            {
                return decimalValue >= 0 ? decimalValue : null;
            }

            if (reader.TryGetDouble(out var doubleValue) &&
                double.IsFinite(doubleValue) &&
                doubleValue >= 0 &&
                doubleValue <= (double)decimal.MaxValue)
            {
                return (decimal)doubleValue;
            }

            reader.Skip();
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();

            if (decimal.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var decimalValue) &&
                decimalValue >= 0)
            {
                return decimalValue;
            }

            return null;
        }

        reader.Skip();
        return null;
    }

    public override void Write(
        Utf8JsonWriter writer,
        decimal? value,
        JsonSerializerOptions options)
    {
        if (value is decimal decimalValue)
        {
            writer.WriteNumberValue(decimalValue);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
