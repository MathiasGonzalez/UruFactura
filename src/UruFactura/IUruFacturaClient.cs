using UruFactura.Cae;
using UruFactura.Models;

namespace UruFactura;

/// <summary>
/// Contrato principal de UruFactura.
/// Implementar esta interfaz (o usar <see cref="UruFacturaClient"/>) permite
/// mockear el cliente en tests de aplicaciones consumidoras.
/// </summary>
public interface IUruFacturaClient : IDisposable
{
    /// <summary>Gestión de CAEs del cliente.</summary>
    ICaeManager Cae { get; }

    // -----------------------------------------------------------------------
    // Generación de CFE
    // -----------------------------------------------------------------------

    /// <summary>Crea un CFE de cualquier tipo soportado (contingencia, cuenta ajena, ...).</summary>
    Cfe Crear(Enums.TipoCfe tipo);

    Cfe CrearETicket();
    Cfe CrearEFactura();
    Cfe CrearERemito();
    Cfe CrearNotaCreditoETicket();
    Cfe CrearNotaDebitoETicket();
    Cfe CrearNotaCreditoEFactura();
    Cfe CrearNotaDebitoEFactura();
    Cfe CrearEFacturaExportacion();
    Cfe CrearNotaCreditoEFacturaExportacion();
    Cfe CrearNotaDebitoEFacturaExportacion();
    Cfe CrearERemitoExportacion();
    Cfe CrearEResguardo();

    // -----------------------------------------------------------------------
    // XML y Firma
    // -----------------------------------------------------------------------

    /// <summary>Carga en el gestor los CAE guardados en el repositorio configurado (llamar al iniciar la app).</summary>
    Task<int> CargarCaesAsync();

    /// <summary>Registra un CAE nuevo y lo guarda en el repositorio configurado (si hay uno).</summary>
    Task RegistrarCaeAsync(Models.Cae cae);

    /// <summary>Toma el próximo número del CAE vigente y completa Numero, Serie y CaeData.</summary>
    void AsignarCae(Cfe cfe);

    /// <summary>Como <see cref="AsignarCae"/> y persiste el número usado en el repositorio de CAE (si hay uno).</summary>
    Task AsignarCaeAsync(Cfe cfe, CancellationToken cancellationToken = default);

    string GenerarXml(Cfe cfe);
    string FirmarCfe(Cfe cfe);
    string GenerarYFirmar(Cfe cfe);

    // -----------------------------------------------------------------------
    // Comunicación con DGI
    // -----------------------------------------------------------------------

    /// <summary>Envía el CFE en un sobre; el resultado del comprobante se consulta luego con el estado del envío.</summary>
    Task<RespuestaDgi> EnviarCfeAsync(Cfe cfe, CancellationToken cancellationToken = default);

    /// <summary>Envía de 1 a 250 CFE en un único sobre.</summary>
    Task<RespuestaDgi> EnviarCfesAsync(IEnumerable<Cfe> cfes, CancellationToken cancellationToken = default);

    /// <summary>Guarda los CFE emitidos en el repositorio de CFE configurado (para los que no se envían en el momento).</summary>
    Task RegistrarCfesAsync(IEnumerable<Cfe> cfes, CancellationToken cancellationToken = default);

    /// <summary>Consulta el resultado de un sobre enviado y actualiza el estado de los CFE indicados.</summary>
    Task<RespuestaDgi> ConsultarEstadoEnvioAsync(RespuestaDgi envio, IEnumerable<Cfe>? cfes = null, CancellationToken cancellationToken = default);

    /// <summary>Consulta el estado de un CFE por tipo, serie y número.</summary>
    Task<RespuestaDgi> ConsultarEstadoCfeAsync(Cfe cfe, CancellationToken cancellationToken = default);

    /// <summary>Envía el reporte diario (resumen firmado) de los CFE emitidos en la fecha.</summary>
    Task<RespuestaReporteDiario> EnviarReporteDiarioAsync(DateTime fecha, IEnumerable<Cfe> cfes, CancellationToken cancellationToken = default);

    /// <summary>Igual que el anterior indicando la secuencia del envío del día (0, 1, ...).</summary>
    Task<RespuestaReporteDiario> EnviarReporteDiarioAsync(DateTime fecha, IEnumerable<Cfe> cfes, int secEnvio, CancellationToken cancellationToken = default);

    /// <summary>Envía el reporte diario con los CFE del repositorio de CFE configurado (los de todas las instancias).</summary>
    Task<RespuestaReporteDiario> EnviarReporteDiarioAsync(DateOnly fecha, int secEnvio = 0, CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------
    // PDF
    // -----------------------------------------------------------------------

    byte[] GenerarPdfA4(Cfe cfe);
    byte[] GenerarPdfTermico(Cfe cfe);
}
