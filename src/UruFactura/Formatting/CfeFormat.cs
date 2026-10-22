using System.Globalization;

namespace UruFactura.Formatting;

internal static class CfeFormat
{
    internal static readonly CultureInfo PdfMonetaryCulture = new("es-UY");

    internal static string DateIso(DateTime value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string DateIso(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string DecimalInvariant(decimal value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);

    internal static string MonetaryPdf(decimal value, string format = "N2") =>
        value.ToString(format, PdfMonetaryCulture);

    /// <summary>
    /// Redondeo comercial (la mitad hacia arriba), el mismo que aplica <c>ToString("F2")</c> al
    /// escribir el XML. <see cref="Math.Round(decimal, int)"/> redondea al par por defecto y daría
    /// montos que no coinciden con los escritos.
    /// </summary>
    internal static decimal Redondear(decimal value, int decimals = 2) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
