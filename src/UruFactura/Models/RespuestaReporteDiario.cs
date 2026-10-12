namespace UruFactura.Models;

/// <summary>
/// Resultado del envío del reporte diario.
/// </summary>
public class RespuestaReporteDiario
{
    /// <summary>Fecha que resume el reporte.</summary>
    public DateTime FechaReporte { get; set; }

    /// <summary>Cantidad de comprobantes resumidos en el reporte.</summary>
    public int CantidadCfe { get; set; }

    /// <summary>Respuesta de la DGI (<c>ACKRepDiario</c>).</summary>
    public RespuestaDgi Respuesta { get; set; } = new();
}
