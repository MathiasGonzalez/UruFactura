namespace UruFactura;

/// <summary>Familia de comprobante según el elemento raíz del esquema CFE de la DGI.</summary>
public enum FamiliaCfe
{
    /// <summary><c>eTck</c>: e-Ticket y sus variantes.</summary>
    Ticket,
    /// <summary><c>eFact</c>: e-Factura y sus variantes.</summary>
    Factura,
    /// <summary><c>eFact_Exp</c>: e-Factura de exportación.</summary>
    FacturaExportacion,
    /// <summary><c>eRem</c>: e-Remito.</summary>
    Remito,
    /// <summary><c>eRem_Exp</c>: e-Remito de exportación.</summary>
    RemitoExportacion,
    /// <summary><c>eResg</c>: e-Resguardo.</summary>
    Resguardo,
    /// <summary><c>eBoleta</c>: e-Boleta de entrada y sus notas.</summary>
    Boleta,
}

/// <summary>Clasificación de los tipos de CFE (familia, notas, contingencia, cuenta ajena).</summary>
public static class TipoCfeExtensions
{
    /// <summary>Familia (elemento raíz del esquema) del tipo de CFE.</summary>
    public static FamiliaCfe Familia(this TipoCfe tipo) => (int)tipo switch
    {
        101 or 102 or 103 or 131 or 132 or 133 or 201 or 202 or 203 or 231 or 232 or 233 => FamiliaCfe.Ticket,
        111 or 112 or 113 or 141 or 142 or 143 or 211 or 212 or 213 or 241 or 242 or 243 => FamiliaCfe.Factura,
        121 or 122 or 123 or 221 or 222 or 223 => FamiliaCfe.FacturaExportacion,
        181 or 281 => FamiliaCfe.Remito,
        124 or 224 => FamiliaCfe.RemitoExportacion,
        182 or 282 => FamiliaCfe.Resguardo,
        151 or 152 or 153 or 251 or 252 or 253 => FamiliaCfe.Boleta,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, $"Tipo de CFE no soportado: {tipo}."),
    };

    /// <summary>Nota de crédito o de débito (requiere referencias).</summary>
    public static bool EsNotaCorreccion(this TipoCfe tipo) => (int)tipo is
        102 or 103 or 112 or 113 or 122 or 123 or 132 or 133 or 142 or 143 or 152 or 153
        or 202 or 203 or 212 or 213 or 222 or 223 or 232 or 233 or 242 or 243 or 252 or 253;

    /// <summary>Nota de crédito (la referencia suele ser un CFE emitido antes).</summary>
    public static bool EsNotaCredito(this TipoCfe tipo) => (int)tipo % 10 == 2 && tipo != TipoCfe.EResguardo
        && tipo != TipoCfe.EResguardoContingencia;

    /// <summary>Comprobante de contingencia (2xx).</summary>
    public static bool EsContingencia(this TipoCfe tipo) => (int)tipo >= 200;

    /// <summary>Venta por cuenta ajena (exige los datos del mandante).</summary>
    public static bool EsCuentaAjena(this TipoCfe tipo) => (int)tipo is
        131 or 132 or 133 or 141 or 142 or 143 or 231 or 232 or 233 or 241 or 242 or 243;

    /// <summary>Tipos de exportación (detalle sin IVA, <c>IndFact</c> 10).</summary>
    public static bool EsExportacion(this TipoCfe tipo) =>
        tipo.Familia() is FamiliaCfe.FacturaExportacion or FamiliaCfe.RemitoExportacion;

    /// <summary>Remitos (sin precios ni totales monetarios).</summary>
    public static bool EsRemito(this TipoCfe tipo) =>
        tipo.Familia() is FamiliaCfe.Remito or FamiliaCfe.RemitoExportacion;

    /// <summary>
    /// Tipos que identifican siempre al receptor: e-Factura, exportación, e-Remito, e-Resguardo y e-Boleta de
    /// entrada (en esta, salvo la compra de moneda extranjera para reventa a un vendedor sin RUT por debajo del
    /// tope). En el e-Ticket es obligatorio por encima del tope de UI o si documenta retenciones. Ninguno lo exige
    /// si la operación está amparada por el secreto profesional (<see cref="Cfe.SecretoProfesional"/>).
    /// </summary>
    public static bool RequiereReceptor(this TipoCfe tipo) => tipo.Familia() != FamiliaCfe.Ticket;
}
