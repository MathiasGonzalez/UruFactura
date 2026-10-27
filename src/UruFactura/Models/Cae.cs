namespace UruFactura;

/// <summary>
/// Constancia de Autorización de Emisión (CAE) de la DGI: autoriza un rango de números de una serie
/// para un tipo de CFE. Se identifica por <see cref="TipoCfe"/>, <see cref="Serie"/> y <see cref="RangoDesde"/>.
/// </summary>
public sealed record Cae
{
    /// <summary>Número de autorización (<c>CAE_ID</c>, hasta 11 dígitos). Se informa en cada CFE.</summary>
    public string NumeroAutorizacion { get; init; } = string.Empty;

    /// <summary>Tipo de CFE que autoriza.</summary>
    public TipoCfe TipoCfe { get; init; }

    /// <summary>Serie de los CFE autorizados: una letra, dos letras o un dígito 1-9 y una letra ("A", "AB", "1A").</summary>
    public string Serie { get; init; } = string.Empty;

    /// <summary>Primer número autorizado.</summary>
    public long RangoDesde { get; init; }

    /// <summary>Último número autorizado.</summary>
    public long RangoHasta { get; init; }

    /// <summary>Fecha de vencimiento.</summary>
    public DateOnly FechaVencimiento { get; init; }

    /// <summary>
    /// Tipo de CAE especial, si la DGI lo entregó como tal (contribuyentes exonerados, IVA mínimo,
    /// Monotributo o Monotributo Social MIDES); <c>null</c> para un CAE común. Se informa en cada CFE
    /// (<c>CAEEspecial</c>) y determina las leyendas de la representación impresa.
    /// </summary>
    public CaeEspecial? Especial { get; init; }

    /// <summary>
    /// Causal del CAE especial (<c>CausalCAEEsp</c>, hasta 3 dígitos), según la tabla de la DGI.
    /// Obligatoria en el CAE exonerado; no corresponde en los demás.
    /// </summary>
    public int? CausalEspecial { get; init; }

    /// <summary>Último número usado (0 si todavía no se usó). Lo actualiza el almacenamiento al numerar.</summary>
    public long UltimoNroUsado { get; init; }

    /// <summary>Cantidad de números que quedan por usar.</summary>
    public long Disponibles => Math.Max(0, RangoHasta - Math.Max(UltimoNroUsado, RangoDesde - 1));

    /// <summary>Porcentaje del rango ya usado (0-100).</summary>
    public decimal PorcentajeUso
    {
        get
        {
            var total = RangoHasta - RangoDesde + 1;
            if (total <= 0) return 100m;
            var usados = Math.Clamp(UltimoNroUsado - RangoDesde + 1, 0, total);
            return Math.Round((decimal)usados / total * 100, 2);
        }
    }

    /// <summary>Indica si el CAE sigue vigente en <paramref name="fecha"/> (vence al final de <see cref="FechaVencimiento"/>).</summary>
    /// <param name="fecha">Fecha a evaluar, en general <c>HoraUruguay.Hoy()</c>.</param>
    public bool EsVigenteEn(DateOnly fecha) => fecha <= FechaVencimiento;

    /// <summary>Indica si comparte números con <paramref name="otro"/>: mismo tipo y serie, y rangos superpuestos.</summary>
    internal bool SeSuperponeCon(Cae otro) =>
        TipoCfe == otro.TipoCfe && Serie == otro.Serie && RangoDesde <= otro.RangoHasta && otro.RangoDesde <= RangoHasta;

    /// <summary>El número que se asigna al reservar (un contador por debajo del rango arranca en <see cref="RangoDesde"/>).</summary>
    internal long ProximoNumero => Math.Max(UltimoNroUsado + 1, RangoDesde);

    /// <summary>Mismo CAE: mismo tipo, serie y comienzo de rango.</summary>
    internal bool EsElMismo(Cae otro) =>
        TipoCfe == otro.TipoCfe && Serie == otro.Serie && RangoDesde == otro.RangoDesde;

    /// <summary>
    /// El CAE con el que se numera <paramref name="tipo"/>: entre los vigentes con números disponibles,
    /// el de vencimiento más lejano y, a igual vencimiento, el que tenga más números libres.
    /// </summary>
    internal static Cae? Elegir(IEnumerable<Cae> caes, TipoCfe tipo, DateOnly hoy) =>
        caes.Where(c => c.TipoCfe == tipo && c.EsVigenteEn(hoy) && c.Disponibles > 0)
            .MaxBy(c => (c.FechaVencimiento, c.Disponibles));

    internal IEnumerable<string> Validar()
    {
        if (NumeroAutorizacion.Length is < 1 or > 11 || !NumeroAutorizacion.All(char.IsAsciiDigit))
            yield return "NumeroAutorizacion debe tener entre 1 y 11 dígitos.";
        if (!Enum.IsDefined(TipoCfe))
            yield return $"El tipo de CFE {(int)TipoCfe} no está soportado.";
        if (!Validacion.EsSerie(Serie))
            yield return "La serie debe ser una letra, dos letras o un dígito 1-9 seguido de una letra (ej: \"A\", \"AB\", \"1A\").";
        if (RangoDesde is < 1 or > 9_999_999 || RangoHasta is < 1 or > 9_999_999 || RangoDesde > RangoHasta)
            yield return "El rango autorizado debe estar entre 1 y 9999999, con RangoDesde ≤ RangoHasta.";
        if (FechaVencimiento == default)
            yield return "FechaVencimiento es obligatoria.";
        if (UltimoNroUsado < 0 || UltimoNroUsado > RangoHasta)
            yield return "UltimoNroUsado debe estar entre 0 y RangoHasta.";
        if (Especial is { } especial && !Enum.IsDefined(especial))
            yield return "Especial no es un tipo de CAE especial válido.";
        if (Especial == CaeEspecial.Exonerado && CausalEspecial is not (>= 0 and <= 999))
            yield return "El CAE exonerado requiere la causal (CausalEspecial, hasta 3 dígitos).";
        else if (Especial != CaeEspecial.Exonerado && CausalEspecial is not null)
            yield return "CausalEspecial solo corresponde al CAE exonerado.";
        if (EsIvaMinimoOMonotributo && Enum.IsDefined(TipoCfe) && TipoCfe.EsCuentaAjena())
            yield return "Los CAE de IVA mínimo y Monotributo no admiten ventas por cuenta ajena.";
    }

    /// <summary>CAE de IVA mínimo, Monotributo o Monotributo Social MIDES: sus CFE informan <c>MntBruto</c> 3 y no discriminan IVA.</summary>
    internal bool EsIvaMinimoOMonotributo => Especial is CaeEspecial.IvaMinimo or CaeEspecial.Monotributo or CaeEspecial.MonotributoSocialMides;

    internal string? ObtenerAdvertencia(DateOnly hoy, int diasAlertaVencimiento, decimal porcentajeAlertaUso)
    {
        var nombre = $"CAE {NumeroAutorizacion} ({TipoCfe}, serie {Serie})";
        if (!EsVigenteEn(hoy))
            return $"⚠️ {nombre} venció el {FechaVencimiento:dd/MM/yyyy}.";
        if (Disponibles == 0)
            return $"⚠️ {nombre} no tiene números disponibles.";

        var diasRestantes = FechaVencimiento.DayNumber - hoy.DayNumber;
        if (diasRestantes <= diasAlertaVencimiento)
            return $"⚠️ {nombre} vence en {diasRestantes} día(s) ({FechaVencimiento:dd/MM/yyyy}).";
        if (PorcentajeUso >= porcentajeAlertaUso)
            return $"⚠️ {nombre} usó el {PorcentajeUso}% de su rango.";
        return null;
    }
}
