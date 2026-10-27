using System.Text.Json;
using System.Text.Json.Serialization;

namespace UruFactura;

/// <summary>
/// Moneda de un CFE (<c>TpoMoneda</c>): un código ISO 4217 de la tabla de monedas de la DGI (<c>TipMonType</c>),
/// que incluye la Unidad Indexada (<c>UYI</c>) y la Unidad Reajustable (<c>UYR</c>). Las de uso frecuente están
/// como propiedades (<see cref="PesoUruguayo"/>, <see cref="DolarAmericano"/>, ...); las demás se crean con su
/// código: <c>new Moneda("CLP")</c>. Una moneda sin inicializar es el peso uruguayo.
/// </summary>
[JsonConverter(typeof(MonedaJsonConverter))]
public readonly struct Moneda : IEquatable<Moneda>
{
    private readonly string? _codigo;

    /// <summary>Crea la moneda con su código ISO 4217 de 3 letras. No lo valida: lo hace la validación del CFE.</summary>
    /// <param name="codigo">Código ISO 4217 (por ejemplo, <c>USD</c>); se ignoran los espacios y las minúsculas.</param>
    public Moneda(string codigo) => _codigo = string.IsNullOrWhiteSpace(codigo) ? null : codigo.Trim().ToUpperInvariant();

    /// <summary>Código ISO 4217 que se informa en el CFE (por ejemplo, <c>UYU</c>).</summary>
    public string Codigo => _codigo ?? "UYU";

    /// <summary>Indica si el código está en la tabla de monedas de la DGI.</summary>
    public bool EsValida => Codigos.Contains(Codigo);

    /// <summary>Peso uruguayo (<c>UYU</c>).</summary>
    public static Moneda PesoUruguayo { get; } = new("UYU");

    /// <summary>Dólar estadounidense (<c>USD</c>).</summary>
    public static Moneda DolarAmericano { get; } = new("USD");

    /// <summary>Euro (<c>EUR</c>).</summary>
    public static Moneda Euro { get; } = new("EUR");

    /// <summary>Peso argentino (<c>ARS</c>).</summary>
    public static Moneda PesoArgentino { get; } = new("ARS");

    /// <summary>Real brasileño (<c>BRL</c>).</summary>
    public static Moneda RealBrasileno { get; } = new("BRL");

    /// <summary>Unidad Indexada uruguaya (<c>UYI</c>).</summary>
    public static Moneda UnidadIndexada { get; } = new("UYI");

    /// <summary>Unidad Reajustable uruguaya (<c>UYR</c>).</summary>
    public static Moneda UnidadReajustable { get; } = new("UYR");

    /// <inheritdoc />
    public bool Equals(Moneda other) => Codigo == other.Codigo;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Moneda other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Codigo.GetHashCode(StringComparison.Ordinal);

    /// <summary>Compara dos monedas por su código.</summary>
    public static bool operator ==(Moneda left, Moneda right) => left.Equals(right);

    /// <summary>Compara dos monedas por su código.</summary>
    public static bool operator !=(Moneda left, Moneda right) => !left.Equals(right);

    /// <summary>El código ISO 4217.</summary>
    public override string ToString() => Codigo;

    /// <summary>Códigos de <c>TipMonType</c> (XSD 1.44.2).</summary>
    private static readonly HashSet<string> Codigos =
    [
        .. ("AED AFN ALL AMD ANG AOA ARS AUD AWG AZM BAM BBD BDT BGN BHD BIF BMD BND BOB BOV BRL BSD BTN BWP BYR BZD CAD " +
            "CDF CHF CLF CLP CNY COP COU CRC CSD CUP CUC CVE CYP CZK DJF DKK DOP DZD EEK EGP ERN ETB EUR FJD FKP GBP GEL " +
            "GHS GIP GMD GNF GTQ GYD HKD HNL HRK HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR KMF KPW KRW KWD " +
            "KYD KZT LAK LBP LKR LRD LSL LTL LVL LYD MAD MDL MGA MKD MMK MNT MOP MRO MTL MUR MVR MWK MXN MXV MYR MZN NAD " +
            "NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RUB RWF SAR SBD SCR SDG SEK SGD SHP SKK SLL SOS " +
            "SRD STD SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD UYU UZS VEF VND VUV WST XAF XAG XAU XBA XBB " +
            "XBC XBD XCD XDR XFO XFU XOF XPD XPF XPT XTS YER ZAR ZMK ZWL UYI UYR AZN BYN CHE CHW MRU RSD SSP STN VES XSU " +
            "XUA ZMW").Split(' '),
    ];
}

/// <summary>Guarda la moneda como su código ISO 4217 (<c>"USD"</c>).</summary>
internal sealed class MonedaJsonConverter : JsonConverter<Moneda>
{
    public override Moneda Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? new Moneda(reader.GetString()!)
            : throw new JsonException("La moneda se guarda como su código ISO 4217.");

    public override void Write(Utf8JsonWriter writer, Moneda value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Codigo);
}
