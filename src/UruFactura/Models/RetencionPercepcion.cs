namespace UruFactura;

/// <summary>
/// Retención, percepción o crédito fiscal de una línea (tabla C del formato CFE; el listado completo está en la web
/// de la DGI): obligatoria en el e-Resguardo y opcional en e-Tickets, e-Facturas y e-Boletas de entrada. Los
/// créditos fiscales tienen código del formulario 2181 (<c>2181xxx</c>).
/// </summary>
public sealed record RetencionPercepcion
{
    /// <summary>
    /// Código (de 1 a 8 dígitos): número de formulario más número de línea, por ejemplo <c>2183211</c>; los
    /// códigos 9999001 a 9999999 son de impuestos no administrados por la DGI.
    /// </summary>
    public string Codigo { get; init; } = string.Empty;

    /// <summary>Tasa en % (opcional, 0-100).</summary>
    public decimal? Tasa { get; init; }

    /// <summary>Monto sujeto a retención (mayor a cero; obligatorio en el e-Resguardo y opcional en los demás).</summary>
    public decimal MontoSujeto { get; init; }

    /// <summary>Valor retenido o percibido (mayor a cero).</summary>
    public decimal Valor { get; init; }

    /// <summary>Información adicional (hasta 150 caracteres, opcional).</summary>
    public string? InfoAdicional { get; init; }
}

/// <summary>
/// Total de un código de retención, percepción o crédito fiscal en el comprobante (<c>RetencPercep</c> de los
/// totales, A-C127 y A-C128). Lo calcula <see cref="Cfe.RetencionesPorCodigo"/>.
/// </summary>
/// <param name="Codigo">Código de la retención, percepción o crédito fiscal.</param>
/// <param name="Valor">Suma de los valores de ese código (en el e-Resguardo, menos los de los ítems a anular).</param>
public sealed record TotalRetencion(string Codigo, decimal Valor)
{
    /// <summary>Si es un crédito fiscal (código del formulario 2181): no suma al total retenido ni al total a pagar.</summary>
    public bool EsCreditoFiscal => Cfe.EsCreditoFiscal(Codigo);
}
