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
