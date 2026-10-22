using UruFactura.Configuration;
using UruFactura.Pdf;

namespace UruFactura;

public partial class UruFacturaClient
{
    /// <summary>
    /// Inicializa el cliente (versión Lite, sin generador de PDF predeterminado).
    /// Las llamadas a <see cref="UruFacturaClient.GenerarPdfA4"/> o
    /// <see cref="UruFacturaClient.GenerarPdfTermico"/> lanzarán
    /// <see cref="InvalidOperationException"/> a menos que se pase un generador personalizado.
    /// </summary>
    /// <param name="config">Configuración de UruFactura.</param>
    public UruFacturaClient(UruFacturaConfig config)
        : this(config, (ICfePdfGenerator?)null)
    {
    }
}
