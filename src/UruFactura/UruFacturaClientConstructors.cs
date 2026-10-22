using UruFactura.Configuration;
using UruFactura.Pdf;

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
}
