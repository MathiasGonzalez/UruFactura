namespace UruFactura;

/// <summary>
/// Acuse de recibo de un sobre (<c>ACKSobre</c>). Que el sobre sea recibido no implica que los CFE
/// hayan sido aceptados: el resultado de cada uno se obtiene con
/// <see cref="IUruFacturaClient.ConsultarEnvioAsync"/>.
/// </summary>
public sealed record AcuseSobre
{
    /// <summary><c>AS</c> recibido, <c>BS</c> rechazado, <c>BA</c> archivo rechazado.</summary>
    public string Estado { get; init; } = string.Empty;

    /// <summary>Indica si la DGI recibió el sobre (<c>AS</c>).</summary>
    public bool Recibido => Estado == "AS";

    /// <summary>Descripción del resultado (incluye los motivos de rechazo).</summary>
    public string Mensaje { get; init; } = string.Empty;

    /// <summary>Motivos de rechazo (código y glosa).</summary>
    public IReadOnlyList<string> Motivos { get; init; } = [];

    /// <summary>Número asignado por la DGI al sobre (para consultar el resultado).</summary>
    public string? IdReceptor { get; init; }

    /// <summary>Token para consultar el resultado.</summary>
    public string? Token { get; init; }

    /// <summary>Fecha y hora a partir de la cual se puede consultar el resultado.</summary>
    public DateTimeOffset? ConsultarDesde { get; init; }

    /// <summary>CFE incluidos en el sobre.</summary>
    public IReadOnlyList<CfeEmitido> Comprobantes { get; init; } = [];

    /// <summary>XML de la respuesta de la DGI.</summary>
    public string? XmlRespuesta { get; init; }
}

/// <summary>Resultado de un sobre (<c>ACKCFE</c>): el estado de cada comprobante.</summary>
public sealed record EstadoEnvio
{
    /// <summary>Estado de cada comprobante del sobre.</summary>
    public IReadOnlyList<EstadoComprobante> Comprobantes { get; init; } = [];

    /// <summary>Indica si la DGI ya procesó el sobre (informó el estado de los comprobantes).</summary>
    public bool Procesado => Comprobantes.Count > 0;

    /// <summary>Indica si la DGI aceptó todos los comprobantes del sobre.</summary>
    public bool TodosAceptados => Procesado && Comprobantes.All(c => c.Aceptado);

    /// <summary>Resumen del resultado.</summary>
    public string Mensaje { get; init; } = string.Empty;

    /// <summary>XML de la respuesta de la DGI.</summary>
    public string? XmlRespuesta { get; init; }
}

/// <summary>Estado de un comprobante informado por la DGI.</summary>
public sealed record EstadoComprobante
{
    /// <summary>Tipo de CFE (101, 111, ...), si la DGI lo informa.</summary>
    public int? TipoCfe { get; init; }

    /// <summary>Serie, si la DGI la informa.</summary>
    public string? Serie { get; init; }

    /// <summary>Número, si la DGI lo informa.</summary>
    public long? Numero { get; init; }

    /// <summary><c>AE</c> aceptado, <c>BE</c> rechazado, <c>CE</c> observado, <c>FE</c> reliquidado.</summary>
    public string Estado { get; init; } = string.Empty;

    /// <summary>Indica si la DGI aceptó el comprobante (<c>AE</c>).</summary>
    public bool Aceptado => Estado == "AE";

    /// <summary>Motivos de rechazo u observaciones (código y glosa).</summary>
    public IReadOnlyList<string> Motivos { get; init; } = [];

    /// <summary>XML de la respuesta de la DGI (solo en la consulta individual).</summary>
    public string? XmlRespuesta { get; init; }

    internal bool Corresponde(CfeEmitido cfe) =>
        TipoCfe == (int)cfe.Tipo && Serie == cfe.Serie && Numero == cfe.Numero;
}

/// <summary>Acuse de recibo del reporte diario (<c>ACKRepDiario</c>).</summary>
public sealed record AcuseReporte
{
    /// <summary><c>AR</c> recibido, <c>BR</c> rechazado, <c>BA</c> archivo rechazado.</summary>
    public string Estado { get; init; } = string.Empty;

    /// <summary>Indica si la DGI recibió el reporte (<c>AR</c>).</summary>
    public bool Recibido => Estado == "AR";

    /// <summary>Descripción del resultado (incluye los motivos de rechazo).</summary>
    public string Mensaje { get; init; } = string.Empty;

    /// <summary>Motivos de rechazo (código y glosa).</summary>
    public IReadOnlyList<string> Motivos { get; init; } = [];

    /// <summary>Número asignado por la DGI al reporte.</summary>
    public string? IdReceptor { get; init; }

    /// <summary>Fecha resumida.</summary>
    public DateOnly Fecha { get; init; }

    /// <summary>Cantidad de CFE resumidos.</summary>
    public int CantidadCfe { get; init; }

    /// <summary>XML de la respuesta de la DGI.</summary>
    public string? XmlRespuesta { get; init; }
}
