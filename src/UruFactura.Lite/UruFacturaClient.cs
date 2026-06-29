using UruFactura.Cae;
using UruFactura.Configuration;
using UruFactura.Pdf;
using UruFactura.Signature;
using UruFactura.Soap;
using UruFactura.Xml;

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

    /// <summary>
    /// Inicializa el cliente con un generador de PDF personalizado.
    /// </summary>
    /// <param name="config">Configuración de UruFactura.</param>
    /// <param name="pdfGenerator">Implementación de <see cref="ICfePdfGenerator"/> a utilizar.</param>
    public UruFacturaClient(UruFacturaConfig config, ICfePdfGenerator? pdfGenerator)
        : this(config,
               new CfeXmlBuilder(),
               new CfeFirmante(config.RutaCertificado, config.PasswordCertificado),
               new CaeManager(),
               new DgiSoapClient(config),
               pdfGenerator)
    {
    }
}
