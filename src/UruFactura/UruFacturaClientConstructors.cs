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
    /// Inicializa el cliente con la configuración provista y el generador de PDF predeterminado
    /// (FluentReport + SkiaSharp + ZXing).
    /// </summary>
    /// <param name="config">Configuración de UruFactura.</param>
    public UruFacturaClient(UruFacturaConfig config)
        : this(config, new CfePdfGenerator(config))
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
               new CfeFirmante(config.RutaCertificado, config.PasswordCertificado, config.AlgoritmoFirma),
               new CaeManager(),
               new DgiSoapClient(config),
               pdfGenerator)
    {
    }
}
