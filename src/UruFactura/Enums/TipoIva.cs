namespace UruFactura;

/// <summary>
/// Indicadores de IVA según DGI.
/// </summary>
public enum TipoIva
{
    /// <summary>Exento de IVA</summary>
    Exento = 1,

    /// <summary>IVA mínimo (10%)</summary>
    Minimo = 2,

    /// <summary>IVA básico (22%)</summary>
    Basico = 3,

    /// <summary>Suspendido</summary>
    Suspendido = 4,
}
