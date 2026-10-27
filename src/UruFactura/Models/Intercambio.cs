namespace UruFactura;

/// <summary>
/// Sobre <c>EnvioCFE_entreEmpresas</c> para un receptor electrónico: los CFE firmados con su adenda. Se
/// entrega por el medio que acuerden las partes (como mínimo, adjunto a un correo con
/// <see cref="NombreArchivo"/>) o con <see cref="IUruFacturaClient.EnviarIntercambioAsync"/> al servicio web
/// del receptor.
/// </summary>
public sealed record SobreReceptor
{
    /// <summary>RUT del receptor electrónico.</summary>
    public required string RutReceptor { get; init; }

    /// <summary>Número asignado al sobre (<c>Idemisor</c>): con él se asocian las respuestas del receptor.</summary>
    public required long IdEmisor { get; init; }

    /// <summary>Fecha y hora de creación del sobre.</summary>
    public required DateTimeOffset Fecha { get; init; }

    /// <summary>XML del sobre.</summary>
    public required string Xml { get; init; }

    /// <summary>Nombre del archivo del sobre: <c>Sob_RUC_AAAAMMDD_IdEmisor.xml</c>.</summary>
    public required string NombreArchivo { get; init; }

    /// <summary>CFE incluidos, en el orden del sobre (el <c>Nro_ordinal</c> de las respuestas es la posición desde 1).</summary>
    public IReadOnlyList<CfeEmitido> Comprobantes { get; init; } = [];
}

/// <summary>
/// Sobre recibido de otro emisor electrónico, ya controlado, con el acuse de recibo firmado que hay que
/// devolverle (<see cref="XmlAcuse"/>). Si fue recibido, la respuesta a cada comprobante se genera con
/// <see cref="IUruFacturaClient.ResponderCfesAsync"/>.
/// </summary>
public sealed record SobreRecibido
{
    /// <summary>RUT del emisor que envió el sobre.</summary>
    public required string RutEmisor { get; init; }

    /// <summary>Número que el emisor asignó al sobre (<c>Idemisor</c>).</summary>
    public required long IdEmisor { get; init; }

    /// <summary>Número que se le asignó al recibirlo (<c>IDReceptor</c> de las respuestas).</summary>
    public required long IdReceptor { get; init; }

    /// <summary>Fecha y hora de creación del sobre (de su carátula).</summary>
    public required DateTimeOffset FechaSobre { get; init; }

    /// <summary>Fecha y hora en que se recibió.</summary>
    public required DateTimeOffset FechaRecepcion { get; init; }

    /// <summary>Nombre del archivo recibido (por correo); <c>null</c> si llegó por servicio web.</summary>
    public string? NombreArchivo { get; init; }

    /// <summary>Recibido (<c>AS</c>) o rechazado (<c>BS</c>, motivos <c>S01</c> a <c>S20</c>).</summary>
    public required EstadoSobre Estado { get; init; }

    /// <summary>Indica si el sobre se recibió (<c>AS</c>).</summary>
    public bool Recibido => Estado == EstadoSobre.Recibido;

    /// <summary>Motivos del rechazo (código, glosa y detalle).</summary>
    public IReadOnlyList<MotivoRechazo> Motivos { get; init; } = [];

    /// <summary>Comprobantes del sobre (vacío si se rechazó: un sobre rechazado no se procesa).</summary>
    public IReadOnlyList<CfeRecibido> Comprobantes { get; init; } = [];

    /// <summary>Acuse de recibo (<c>ACKSobre</c>) firmado, para devolver al emisor.</summary>
    public required string XmlAcuse { get; init; }

    /// <summary>Nombre del archivo del acuse: <c>M_IDRespuesta_Sob_RUC_AAAAMMDD_IdEmisor.xml</c>.</summary>
    public required string NombreArchivoAcuse { get; init; }
}

/// <summary>
/// CFE recibido de un proveedor en un sobre, con el resultado de los controles: firma, numeración dentro
/// del CAE, vigencia del CAE y formato. Hay que conservar el <see cref="Xml"/> firmado.
/// </summary>
public sealed record CfeRecibido
{
    /// <summary>Posición en el sobre, desde 1 (<c>Nro_ordinal</c>).</summary>
    public required int NroOrdinal { get; init; }

    /// <summary>XML del CFE firmado, tal como llegó.</summary>
    public required string Xml { get; init; }

    /// <summary>Adenda (información comercial, fuera de la firma), si la tiene.</summary>
    public string? Adenda { get; init; }

    /// <summary>RUT del emisor del CFE.</summary>
    public string? RutEmisor { get; init; }

    /// <summary>Tipo de CFE (puede ser uno que la librería no emite).</summary>
    public TipoCfe? Tipo { get; init; }

    /// <summary>Serie.</summary>
    public string? Serie { get; init; }

    /// <summary>Número.</summary>
    public long? Numero { get; init; }

    /// <summary>Fecha del comprobante (<c>FchEmis</c>).</summary>
    public DateOnly? FechaEmision { get; init; }

    /// <summary>Fecha y hora de la firma (<c>TmstFirma</c>).</summary>
    public DateTimeOffset? FechaFirma { get; init; }

    /// <summary>Moneda (código ISO 4217, p. ej. <c>UYU</c>).</summary>
    public string? Moneda { get; init; }

    /// <summary>Monto total a pagar (<c>MntPagar</c>) o, si no lo tiene, el total (<c>MntTotal</c>).</summary>
    public decimal? Monto { get; init; }

    /// <summary>Resultado de los controles: aceptado (<c>AE</c>) o rechazado (<c>BE</c>).</summary>
    public required EstadoCfe Estado { get; init; }

    /// <summary>Motivos del rechazo detectados al recibirlo.</summary>
    public IReadOnlyList<MotivoRechazo> Motivos { get; init; } = [];
}

/// <summary>
/// Motivo de rechazo de un sobre (<c>S01</c> a <c>S20</c>) o de un CFE (<c>E01</c> a <c>E60</c>), según el
/// formato del mensaje de respuesta de la DGI (v19). Un receptor puede rechazar un CFE con los motivos
/// <c>E02</c> a <c>E07</c>, <c>E10</c> a <c>E12</c> y los comerciales <c>E20</c> a <c>E60</c>.
/// </summary>
/// <param name="Codigo">Código (p. ej. <c>E24</c>).</param>
/// <param name="Glosa">Descripción según la tabla de la DGI (hasta 50 caracteres; se recorta).</param>
/// <param name="Detalle">Descripción detallada (hasta 500 caracteres, opcional).</param>
public sealed record MotivoRechazo(string Codigo, string Glosa, string? Detalle = null)
{
    /// <summary>E20: orden de compra vencida.</summary>
    public static MotivoRechazo OrdenDeCompraVencida(string? detalle = null) => new("E20", "Orden de compra vencida", detalle);

    /// <summary>E21: mercadería en mal estado.</summary>
    public static MotivoRechazo MercaderiaEnMalEstado(string? detalle = null) => new("E21", "Mercadería en mal estado", detalle);

    /// <summary>E22: proveedor inhabilitado por organismo de contralor.</summary>
    public static MotivoRechazo ProveedorInhabilitado(string? detalle = null) => new("E22", "Proveedor inhabilitado por organismo de contralor", detalle);

    /// <summary>E23: contraprestación no recibida.</summary>
    public static MotivoRechazo ContraprestacionNoRecibida(string? detalle = null) => new("E23", "Contraprestación no recibida", detalle);

    /// <summary>E24: diferencia de precios y/o descuentos.</summary>
    public static MotivoRechazo DiferenciaDePrecios(string? detalle = null) => new("E24", "Diferencia precios y/o descuentos", detalle);

    /// <summary>E25: factura con error de cálculos.</summary>
    public static MotivoRechazo ErrorDeCalculos(string? detalle = null) => new("E25", "Factura con error cálculos", detalle);

    /// <summary>E26: diferencia con plazos.</summary>
    public static MotivoRechazo DiferenciaConPlazos(string? detalle = null) => new("E26", "Diferencia con plazos", detalle);

    /// <inheritdoc />
    public override string ToString() => string.IsNullOrWhiteSpace(Detalle) ? $"{Codigo} {Glosa}" : $"{Codigo} {Glosa}: {Detalle}";
}

/// <summary>Respuesta firmada (<c>ACKCFE</c>) a los comprobantes de un sobre recibido, para devolver al emisor.</summary>
public sealed record RespuestaCfes
{
    /// <summary>XML firmado de la respuesta.</summary>
    public required string Xml { get; init; }

    /// <summary>Nombre del archivo: <c>ME_IDRespuesta_Sob_RUC_AAAAMMDD_IdEmisor.xml</c>.</summary>
    public required string NombreArchivo { get; init; }

    /// <summary>Estado informado de cada comprobante: aceptado (<c>AE</c>) o rechazado (<c>BE</c>) con sus motivos.</summary>
    public IReadOnlyList<EstadoComprobante> Comprobantes { get; init; } = [];
}

/// <summary>
/// Comunicación firmada (<c>CFEs_Anulados_x_RechazoDGI</c>) al receptor electrónico de los CFE que se le
/// enviaron y la DGI rechazó, que por lo tanto quedan anulados. No se envía a la DGI.
/// </summary>
public sealed record NotificacionAnulacion
{
    /// <summary>RUT del receptor electrónico.</summary>
    public required string RutReceptor { get; init; }

    /// <summary>Número de la comunicación (<c>IDComunicacion</c>).</summary>
    public required long IdComunicacion { get; init; }

    /// <summary>XML firmado de la comunicación.</summary>
    public required string Xml { get; init; }

    /// <summary>Nombre del archivo: <c>Rech_RUC_IdComunicacion.xml</c>.</summary>
    public required string NombreArchivo { get; init; }

    /// <summary>CFE que se comunican como anulados.</summary>
    public IReadOnlyList<CfeEmitido> Comprobantes { get; init; } = [];
}

/// <summary>CFE de un proveedor que la DGI rechazó y él comunicó como anulado.</summary>
/// <param name="RutEmisor">RUT del proveedor que lo emitió.</param>
/// <param name="Tipo">Tipo de CFE.</param>
/// <param name="Serie">Serie.</param>
/// <param name="Numero">Número.</param>
/// <param name="Fecha">Fecha del comprobante.</param>
public sealed record CfeAnulado(string RutEmisor, TipoCfe Tipo, string Serie, long Numero, DateOnly Fecha);

/// <summary>Operación del servicio web de intercambio entre emisores electrónicos (<c>ws_efactura_intercambio</c>).</summary>
public enum OperacionIntercambio
{
    /// <summary>Envío de un sobre con CFE (<c>EnvioCFE_entreEmpresas</c>).</summary>
    Sobre,

    /// <summary>Acuse de recibo de un sobre (<c>ACKSobre</c>).</summary>
    RespuestaSobre,

    /// <summary>Respuesta a los comprobantes de un sobre (<c>ACKCFE</c>).</summary>
    RespuestaCfes,

    /// <summary>Comunicación de CFE anulados por rechazo de la DGI (<c>CFEs_Anulados_x_RechazoDGI</c>).</summary>
    NotificacionAnulacion,
}

/// <summary>
/// Respuesta del servicio web de intercambio de otro emisor: <c>x000</c> recibido, <c>x001</c> rechazado y
/// <c>x002</c> error al recibir (hay que reintentar más tarde), donde <c>x</c> es 1 (sobre), 2 (acuse del
/// sobre), 3 (respuesta a los comprobantes) o 4 (anulación).
/// </summary>
/// <param name="Codigo">Código de la respuesta.</param>
/// <param name="Descripcion">Descripción.</param>
/// <param name="Detalle">Detalle, si lo hay.</param>
public sealed record RespuestaIntercambio(int Codigo, string Descripcion, string? Detalle)
{
    /// <summary>Indica si el receptor recibió el mensaje (<c>1000</c>, <c>2000</c>, <c>3000</c> o <c>4000</c>).</summary>
    public bool Recibido => Codigo is 1000 or 2000 or 3000 or 4000;

    /// <summary>Indica si el receptor no pudo recibirlo por un error suyo y hay que reintentar más tarde (<c>x002</c>).</summary>
    public bool Reintentar => Codigo is 1002 or 2002 or 3002 or 4002;
}

/// <summary>Emisor electrónico del padrón de la DGI, con sus datos de contacto para el intercambio.</summary>
public sealed record EmisorElectronico
{
    /// <summary>RUT.</summary>
    public required string Rut { get; init; }

    /// <summary>Denominación.</summary>
    public string? Denominacion { get; init; }

    /// <summary>Fecha desde la que es emisor electrónico.</summary>
    public DateOnly? FechaInicio { get; init; }

    /// <summary>Fin del período de transición, si lo tuvo.</summary>
    public DateOnly? FechaFinTransicion { get; init; }

    /// <summary>Fecha en que dejó de ser emisor electrónico (<c>null</c> si lo sigue siendo).</summary>
    public DateOnly? FechaFin { get; init; }

    /// <summary>Correo para el intercambio de CFE con otros emisores.</summary>
    public string? Correo { get; init; }

    /// <summary>URL de su servicio web de intercambio (<c>ws_efactura_intercambio</c>), si lo publicó.</summary>
    public string? UrlServicioWeb { get; init; }

    /// <summary>Correo de contacto técnico.</summary>
    public string? CorreoContactoTecnico { get; init; }

    /// <summary>Indica si es emisor electrónico en <paramref name="fecha"/>: un receptor electrónico, al que se le envían los CFE.</summary>
    public bool EsEmisorEn(DateOnly fecha) => (FechaInicio is null || FechaInicio <= fecha) && (FechaFin is null || fecha <= FechaFin);
}
