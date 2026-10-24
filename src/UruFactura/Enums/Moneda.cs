namespace UruFactura;

/// <summary>
/// Indicadores de tipo de moneda según nomenclatura DGI / ISO 4217.
/// El Peso Uruguayo utiliza el valor 0 (código interno DGI para moneda local).
/// Los demás valores siguen los códigos numéricos ISO 4217.
/// </summary>
public enum Moneda
{
    PesoUruguayo = 0,
    DolarAmericano = 840,
    Euro = 978,
}

/// <summary>Códigos de las monedas.</summary>
public static class MonedaExtensions
{
    /// <summary>Código ISO 4217 de 3 letras que usa la DGI (<c>TpoMoneda</c>): UYU, USD o EUR.</summary>
    public static string CodigoIso(this Moneda moneda) => moneda switch
    {
        Moneda.PesoUruguayo => "UYU",
        Moneda.DolarAmericano => "USD",
        Moneda.Euro => "EUR",
        _ => throw new ArgumentOutOfRangeException(nameof(moneda), moneda, "Moneda sin código ISO 4217 definido."),
    };
}
