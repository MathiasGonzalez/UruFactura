namespace UruFactura.Models;

/// <summary>
/// Datos de la Constancia de Autorización de Emisión que se incluyen en cada CFE
/// (elemento <c>CAEData</c> del formato CFE de la DGI).
/// </summary>
public class CaeData
{
    /// <summary>Número de autorización del CAE (<c>CAE_ID</c>, hasta 11 dígitos).</summary>
    public string NumeroAutorizacion { get; set; } = string.Empty;

    /// <summary>Primer número del rango autorizado (<c>DNro</c>).</summary>
    public long RangoDesde { get; set; }

    /// <summary>Último número del rango autorizado (<c>HNro</c>).</summary>
    public long RangoHasta { get; set; }

    /// <summary>Fecha de vencimiento del CAE (<c>FecVenc</c>).</summary>
    public DateOnly FechaVencimiento { get; set; }

    /// <summary>Crea los datos a partir de un <see cref="Cae"/> registrado.</summary>
    public static CaeData Desde(Cae cae) => new()
    {
        NumeroAutorizacion = cae.NumeroAutorizacion,
        RangoDesde         = cae.RangoDesde,
        RangoHasta         = cae.RangoHasta,
        FechaVencimiento   = cae.FechaVencimiento,
    };

    internal IEnumerable<string> Validar(long numero)
    {
        if (NumeroAutorizacion.Length is < 1 or > 11 || !NumeroAutorizacion.All(char.IsAsciiDigit))
            yield return "CaeData.NumeroAutorizacion debe tener entre 1 y 11 dígitos.";

        if (RangoDesde is < 1 or > 9_999_999 || RangoHasta is < 1 or > 9_999_999 || RangoDesde > RangoHasta)
            yield return "CaeData: el rango autorizado (DNro-HNro) es inválido.";
        else if (numero > 0 && (numero < RangoDesde || numero > RangoHasta))
            yield return $"El número {numero} está fuera del rango del CAE ({RangoDesde}-{RangoHasta}).";

        if (FechaVencimiento == default)
            yield return "CaeData.FechaVencimiento es obligatoria.";
    }
}
