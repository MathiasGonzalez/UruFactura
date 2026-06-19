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
    Cfe CrearERemitoDespachante();
    Cfe CrearEResguardo();
    Cfe CrearNotaCreditoERemito();

    // -----------------------------------------------------------------------
    // XML y Firma
    // -----------------------------------------------------------------------

    string GenerarXml(Cfe cfe);
    string FirmarCfe(Cfe cfe);
    string GenerarYFirmar(Cfe cfe);

    // -----------------------------------------------------------------------
    // Comunicación con DGI
    // -----------------------------------------------------------------------

    Task<RespuestaDgi> EnviarCfeAsync(Cfe cfe, CancellationToken cancellationToken = default);
    Task<RespuestaDgi> ConsultarEstadoCfeAsync(Cfe cfe, CancellationToken cancellationToken = default);
    Task<RespuestaReporteDiario> EnviarReporteDiarioAsync(DateTime fecha, IEnumerable<Cfe> cfes, CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------
    // PDF
    // -----------------------------------------------------------------------

    byte[] GenerarPdfA4(Cfe cfe);
    byte[] GenerarPdfTermico(Cfe cfe);
}
