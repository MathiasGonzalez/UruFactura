namespace UruFactura;

/// <summary>
/// Indicador del motivo de traslado para e-Remito (181) y e-Remito Despachante (131),
/// según el esquema e-CFE de la DGI de Uruguay.
/// </summary>
public enum IndTraslado
{
    /// <summary>Traslado propio (entre depósitos/locales del mismo emisor) — 1</summary>
    TrasladoPropio = 1,

    /// <summary>Traslado en comisión — 2</summary>
    TrasladoEnComision = 2,

    /// <summary>Devolución al proveedor — 3</summary>
    Devolucion = 3,

    /// <summary>Traslado por venta — 4</summary>
    TrasladoPorVenta = 4,

    /// <summary>Traslado en consignación — 5</summary>
    TrasladoEnConsignacion = 5,

    /// <summary>Traslado por exposición / feria — 6</summary>
    TrasladoPorExposicion = 6,
}
