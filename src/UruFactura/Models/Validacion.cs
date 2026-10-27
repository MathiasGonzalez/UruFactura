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

    /// <summary>
    /// Código de país (<c>CodPaisType</c>): uno de los códigos ISO 3166-1 alfa-2 que admite la DGI, o "99" (país que
    /// no está en la tabla) si <paramref name="admiteSinTabla"/>.
    /// </summary>
    internal void CodigoPais(string campo, string? valor, bool admiteSinTabla = false)
    {
        if (valor is null || !CodigosPais.Contains(valor) && !(admiteSinTabla && valor == "99"))
            Error(campo, admiteSinTabla
                ? "Debe ser un código de país ISO 3166-1 de 2 letras admitido por la DGI (ej: \"US\"), o \"99\" si el país no está en la tabla."
                : "Debe ser un código de país ISO 3166-1 de 2 letras admitido por la DGI (ej: \"UY\", \"US\").");
    }

    /// <summary>País de un documento: el indicado, en mayúsculas, o "UY" si se omite.</summary>
    internal static string PaisDocumento(string? codigoPais) =>
        string.IsNullOrWhiteSpace(codigoPais) ? "UY" : codigoPais.Trim().ToUpperInvariant();

    /// <summary>Documentos uruguayos (NIE, RUT y C.I.): van en <c>DocRecep</c> (no en <c>DocRecepExt</c>) y son del país UY.</summary>
    internal static bool EsDocumentoUruguayo(TipoDocumentoReceptor tipo) =>
        tipo is TipoDocumentoReceptor.Nie or TipoDocumentoReceptor.Rut or TipoDocumentoReceptor.CedulaIdentidad;

    /// <summary>
    /// Documento de identificación del receptor, del mandante o del propietario de la mercadería (A-C60 a A-C62.1,
    /// K-C2 a K-C4): los uruguayos son del país UY, de hasta 12 caracteres y, el RUT y la C.I., con su dígito
    /// verificador (el del NIE lo controla la DGI: el algoritmo no está publicado); el DNI es de AR, BR, CL o PY; el
    /// resto, de cualquier país de la tabla (o "99") y de hasta 20 caracteres.
    /// </summary>
    /// <param name="prefijo">Ruta del objeto (<c>Receptor</c>, <c>Mandante</c>, <c>Propietario</c>).</param>
    internal void Documento(string prefijo, TipoDocumentoReceptor tipo, string? codigoPais, string? documento)
    {
        if (!Enum.IsDefined(tipo))
        {
            Error($"{prefijo}.TipoDocumento", "No es válido.");
            return;
        }

        var pais = PaisDocumento(codigoPais);
        var campo = $"{prefijo}.Documento";
        if (EsDocumentoUruguayo(tipo))
        {
            if (pais != "UY")
                Error($"{prefijo}.CodigoPais", "Los documentos uruguayos (NIE, RUT y C.I.) son del país \"UY\".");
            if (string.IsNullOrWhiteSpace(documento))
                Error(campo, "Es obligatorio.");
            else if (tipo == TipoDocumentoReceptor.Rut && !UruFactura.Rut.EsValido(documento))
                Error(campo, "El RUT debe tener 12 dígitos y un dígito verificador válido.");
            else if (tipo == TipoDocumentoReceptor.CedulaIdentidad && !Ci.EsValida(documento))
                Error(campo, "La C.I. debe tener de 6 a 8 dígitos, sin puntos ni guión, y un dígito verificador válido.");
            else if (tipo == TipoDocumentoReceptor.Nie && !(documento.Length <= 12 && documento.All(char.IsAsciiDigit)))
                Error(campo, "El NIE debe tener hasta 12 dígitos, sin puntos ni guión.");
            return;
        }

        if (tipo == TipoDocumentoReceptor.Dni)
        {
            if (pais is not ("AR" or "BR" or "CL" or "PY"))
                Error($"{prefijo}.CodigoPais", "El DNI es de Argentina, Brasil, Chile o Paraguay (\"AR\", \"BR\", \"CL\" o \"PY\").");
        }
        else
            CodigoPais($"{prefijo}.CodigoPais", pais, admiteSinTabla: true);
        Texto(campo, documento, 20, obligatorio: true);
    }

    /// <summary>
    /// Código GTIN (<c>GTIN8</c>, <c>GTIN12</c>, <c>GTIN13</c> o <c>GTIN14</c>): los dígitos de su largo y el dígito
    /// verificador del estándar GS1 (módulo 10, ponderando 3 y 1 desde la derecha).
    /// </summary>
    internal static bool EsGtin(string tipo, string codigo)
    {
        var largo = tipo switch { "GTIN8" => 8, "GTIN12" => 12, "GTIN13" => 13, "GTIN14" => 14, _ => 0 };
        if (codigo.Length != largo || !codigo.All(char.IsAsciiDigit))
            return false;
        var suma = 0;
        for (var i = largo - 2; i >= 0; i--)
            suma += (codigo[i] - '0') * ((largo - 2 - i) % 2 == 0 ? 3 : 1);
        return (10 - suma % 10) % 10 == codigo[^1] - '0';
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
