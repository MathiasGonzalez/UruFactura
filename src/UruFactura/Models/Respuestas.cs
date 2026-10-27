namespace UruFactura;

/// <summary>
/// Acuse de recibo de un sobre (<c>ACKSobre</c>). Que el sobre sea recibido no implica que los CFE
/// hayan sido aceptados: el resultado de cada uno se obtiene con
/// <see cref="IUruFacturaClient.ConsultarEnvioAsync"/>.
/// </summary>
public sealed record AcuseSobre
{
    /// <summary>Estado del acuse: recibido (<c>AS</c>), rechazado (<c>BS</c>) o archivo rechazado (<c>BA</c>).</summary>
    public required EstadoSobre Estado { get; init; }

    /// <summary>Indica si la DGI recibió el sobre (<c>AS</c>).</summary>
    public bool Recibido => Estado == EstadoSobre.Recibido;

    /// <summary>Descripción del resultado (incluye los motivos de rechazo).</summary>
    public string Mensaje { get; init; } = string.Empty;

    /// <summary>Motivos de rechazo (código y glosa).</summary>
    public IReadOnlyList<string> Motivos { get; init; } = [];

    /// <summary>RUT de quien acusa recibo: la DGI o el receptor electrónico.</summary>
    public string? RutReceptor { get; init; }

    /// <summary>Número que el emisor asignó al sobre que se acusa (<c>Idemisor</c> del sobre).</summary>
    public long? IdEmisor { get; init; }

    /// <summary>Número asignado al sobre por quien lo recibió (para consultar el resultado).</summary>
    public string? IdReceptor { get; init; }

    /// <summary>Token para consultar el resultado.</summary>
    public string? Token { get; init; }

    /// <summary>Fecha y hora a partir de la cual se puede consultar el resultado.</summary>
    public DateTimeOffset? ConsultarDesde { get; init; }

    /// <summary>CFE incluidos en el sobre, en el orden en que se enviaron.</summary>
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

    /// <summary>RUT de quien responde: la DGI o el receptor electrónico.</summary>
    public string? RutReceptor { get; init; }

    /// <summary>Número que el emisor asignó al sobre que se responde (<c>Idemisor</c> del sobre).</summary>
    public long? IdEmisor { get; init; }

    /// <summary>Resumen del resultado.</summary>
    public string Mensaje { get; init; } = string.Empty;

    /// <summary>XML de la respuesta de la DGI.</summary>
    public string? XmlRespuesta { get; init; }
}

/// <summary>Estado de un comprobante informado por la DGI.</summary>
public sealed record EstadoComprobante
{
    /// <summary>
    /// Posición del comprobante en el sobre (<c>Nro_ordinal</c>, desde 1). Es el único dato que el
    /// resultado de un envío trae siempre: tipo, serie y número son opcionales.
    /// </summary>
    public int? NroOrdinal { get; init; }

    /// <summary>Tipo de CFE (101, 111, ...), si la DGI lo informa.</summary>
    public int? TipoCfe { get; init; }

    /// <summary>Serie, si la DGI la informa.</summary>
    public string? Serie { get; init; }

    /// <summary>Número, si la DGI lo informa.</summary>
    public long? Numero { get; init; }

    /// <summary>Estado del comprobante.</summary>
    public required EstadoCfe Estado { get; init; }

    /// <summary>Indica si la DGI aceptó el comprobante (<c>AE</c>).</summary>
    public bool Aceptado => Estado == EstadoCfe.Aceptado;

    /// <summary>Motivos de rechazo u observaciones (código y glosa).</summary>
    public IReadOnlyList<string> Motivos { get; init; } = [];

    /// <summary>XML de la respuesta de la DGI (solo en la consulta individual).</summary>
    public string? XmlRespuesta { get; init; }

    /// <summary>
    /// Si es el resultado de <paramref name="cfe"/>, enviado en la posición <paramref name="ordinal"/>
    /// del sobre: por tipo, serie y número cuando la DGI los informa y, si no, por la posición.
    /// </summary>
    internal bool Corresponde(CfeEmitido cfe, int ordinal) =>
        TipoCfe is null && Serie is null && Numero is null
            ? NroOrdinal == ordinal
            : (TipoCfe is null || TipoCfe == (int)cfe.Tipo)
              && (Serie is null || Serie == cfe.Serie)
              && (Numero is null || Numero == cfe.Numero);
}

/// <summary>Acuse de recibo del reporte diario (<c>ACKRepDiario</c>).</summary>
public sealed record AcuseReporte
{
    /// <summary>Estado del acuse: recibido (<c>AR</c>), rechazado (<c>BR</c>) o archivo rechazado (<c>BA</c>).</summary>
    public required EstadoReporte Estado { get; init; }

    /// <summary>Indica si la DGI recibió el reporte (<c>AR</c>).</summary>
    public bool Recibido => Estado == EstadoReporte.Recibido;

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
