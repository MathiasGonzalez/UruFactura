namespace UruFactura;

/// <summary>Estado de un comprobante informado por la DGI (<c>ACKCFE</c> y consulta de estado).</summary>
public enum EstadoCfe
{
    /// <summary><c>AE</c>: comprobante aceptado.</summary>
    Aceptado,

    /// <summary><c>BE</c>: comprobante rechazado. No se reenvía: se emite uno nuevo.</summary>
    Rechazado,

    /// <summary><c>CE</c>: comprobante de contingencia observado.</summary>
    Observado,

    /// <summary><c>FE</c>: comprobante reliquidado.</summary>
    Reliquidado,
}

/// <summary>Estado del acuse de un sobre (<c>ACKSobre</c>).</summary>
public enum EstadoSobre
{
    /// <summary><c>AS</c>: sobre recibido. No implica que los CFE hayan sido aceptados.</summary>
    Recibido,

    /// <summary><c>BS</c>: sobre rechazado (motivos <c>S01</c> a <c>S20</c>).</summary>
    Rechazado,

    /// <summary><c>BA</c>: archivo rechazado.</summary>
    ArchivoRechazado,
}

/// <summary>Estado del acuse del reporte diario (<c>ACKRepDiario</c>).</summary>
public enum EstadoReporte
{
    /// <summary><c>AR</c>: reporte recibido.</summary>
    Recibido,

    /// <summary><c>BR</c>: reporte rechazado.</summary>
    Rechazado,

    /// <summary><c>BA</c>: archivo rechazado.</summary>
    ArchivoRechazado,
}
