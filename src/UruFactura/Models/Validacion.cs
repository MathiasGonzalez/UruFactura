using System.Xml;
using UruFactura.Formatting;

namespace UruFactura;

/// <summary>
/// Acumula los errores de validación de un CFE y concentra las restricciones de los tipos del
/// esquema de la DGI (<c>DGITypes.xsd</c>): largos, montos, fechas, series y códigos de país.
/// </summary>
internal sealed class Validacion
{
    /// <summary>Máximo de un monto (<c>MontoType</c>: 15 enteros y 2 decimales).</summary>
    internal const decimal MontoMaximo = 999_999_999_999_999.99m;

    /// <summary>Máximo de un precio unitario (<c>PrecioUnitarioType</c>: 11 enteros y 6 decimales).</summary>
    internal const decimal PrecioMaximo = 99_999_999_999.999999m;

    /// <summary>Máximo de una cantidad (<c>CantType</c>: 14 enteros y 3 decimales).</summary>
    internal const decimal CantidadMaxima = 99_999_999_999_999.999m;

    /// <summary>Rango de <c>FechaType</c>.</summary>
    internal static readonly DateOnly FechaMinima = new(2011, 10, 1), FechaMaxima = new(2050, 12, 31);

    internal List<ErrorValidacion> Errores { get; } = [];

    internal void Error(string campo, string mensaje) => Errores.Add(new ErrorValidacion(campo, mensaje));

    /// <summary>
    /// Texto: obligatorio (si se indica), de hasta <paramref name="maximo"/> caracteres y sin caracteres
    /// que XML no admite, como los de control que se cuelan al pegar texto o con lectores de códigos.
    /// </summary>
    internal void Texto(string campo, string? valor, int maximo, bool obligatorio = false)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            if (obligatorio)
                Error(campo, "Es obligatorio.");
        }
        else if (valor.Length > maximo)
            Error(campo, $"Admite hasta {maximo} caracteres.");
        else if (!EsTextoXml(valor))
            Error(campo, "Contiene caracteres de control no admitidos.");
    }

    /// <summary>Monto con el rango de <c>MontoType</c> (o de <c>Monto_admite_negType</c>).</summary>
    internal void Monto(string campo, decimal valor, bool admiteNegativo = false)
    {
        if (!admiteNegativo && valor < 0)
            Error(campo, "No puede ser negativo.");
        else if (Math.Abs(valor) > MontoMaximo)
            Error(campo, "Supera el máximo del formato CFE (15 dígitos enteros).");
    }

    /// <summary>Fecha dentro del rango de <c>FechaType</c> (01/10/2011 al 31/12/2050).</summary>
    internal void Fecha(string campo, DateOnly fecha)
    {
        if (fecha < FechaMinima || fecha > FechaMaxima)
            Error(campo, "Debe estar entre el 01/10/2011 y el 31/12/2050.");
    }

    /// <summary>Tipo de cambio (<c>TipCambioType</c>): de 0,001 a 9999,999 con 3 decimales.</summary>
    internal void TipoCambio(string campo, decimal valor)
    {
        if (CfeFormat.Redondear(valor, 3) is < 0.001m or > 9999.999m)
            Error(campo, "Debe estar entre 0,001 y 9999,999.");
    }

    /// <summary>Código de país (<c>CodPaisType</c>): uno de los códigos ISO 3166-1 alfa-2 que admite la DGI.</summary>
    internal void CodigoPais(string campo, string? valor)
    {
        if (valor is null || !CodigosPais.Contains(valor))
            Error(campo, "Debe ser un código de país ISO 3166-1 de 2 letras admitido por la DGI (ej: \"UY\", \"US\").");
    }

    /// <summary>Serie (<c>SerieType</c>): una letra, dos letras o un dígito 1-9 y una letra ("A", "AB", "1A").</summary>
    internal static bool EsSerie(string? serie) =>
        serie is { Length: 1 } && char.IsAsciiLetterUpper(serie[0])
        || serie is { Length: 2 }
           && (char.IsAsciiLetterUpper(serie[0]) && char.IsAsciiLetterUpper(serie[1])
               || serie[0] is >= '1' and <= '9' && char.IsAsciiLetterUpper(serie[1]));

    /// <summary>Indica si todos los caracteres del texto son válidos en XML 1.0.</summary>
    internal static bool EsTextoXml(string texto)
    {
        for (var i = 0; i < texto.Length; i++)
        {
            if (XmlConvert.IsXmlChar(texto[i]))
                continue;
            if (i + 1 < texto.Length && XmlConvert.IsXmlSurrogatePair(texto[i + 1], texto[i]))
            {
                i++;
                continue;
            }
            return false;
        }
        return true;
    }

    /// <summary>Códigos de país de <c>CodPaisType</c> (XSD 1.44.2).</summary>
    private static readonly HashSet<string> CodigosPais =
    [
        .. ("AD AE AF AG AI AL AM AN AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BU BV " +
            "BW BY BZ CA CC CD CF CG CH CI CK CL CM CN CO CR CS CU CV CW CX CY CZ DE DJ DK DM DO DZ EA EC EE EG EH ER ES " +
            "ET FI FJ FK FM FO FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM " +
            "IN IO IQ IR IS IT JE JM JO JP KA KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD " +
            "ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF " +
            "PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SU SV " +
            "SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TP TR TT TV TW TZ UA UE UG UM US UY UZ VA VC VE VG VI VN VU WF WS " +
            "YD YE YT YU ZA ZM ZR ZW").Split(' '),
    ];
}
