using UruFactura.Enums;
using UruFactura.Models;

namespace UruFactura.Pdf;

/// <summary>
/// Implementación predeterminada de <see cref="ICfeQrGenerator"/>.
/// Delega en los métodos estáticos de <see cref="CfePdfGenerator"/>.
/// </summary>
public class CfeQrGenerator : ICfeQrGenerator
{
    /// <inheritdoc />
    public virtual byte[] GenerarQrCode(Cfe cfe) =>
        CfePdfGenerator.GenerarQrCode(cfe);

    /// <inheritdoc />
    public virtual byte[] GenerarQrCode(Cfe cfe, Ambiente ambiente) =>
        CfePdfGenerator.GenerarQrCode(cfe, ambiente);
}
