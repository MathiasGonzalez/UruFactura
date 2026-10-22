using System.Globalization;
using UruFactura.Formatting;

namespace UruFactura;

/// <summary>
/// CFE emitido: numerado con un CAE, firmado y registrado. Lo devuelve
/// <see cref="IUruFacturaClient.EmitirAsync"/>. Los datos fiscales son inmutables; solo cambia el
/// estado ante la DGI (<see cref="EstadoDgi"/>) al enviarlo y consultarlo.
/// </summary>
public sealed class CfeEmitido
{
    /// <summary>
    /// Reconstruye un CFE emitido (para implementaciones de <see cref="IUruFacturaStore"/>).
    /// </summary>
    /// <param name="documento">Contenido emitido, con <see cref="Cfe.FechaEmision"/> y <see cref="Cfe.Emisor"/> resueltos.</param>
    /// <param name="cae">CAE con el que se numeró.</param>
    /// <param name="numero">Número asignado.</param>
    /// <param name="totales">Totales informados en el XML.</param>
    /// <param name="fechaFirma">Fecha y hora de la firma (<c>TmstFirma</c>).</param>
    /// <param name="codigoSeguridad">Código de seguridad (<c>DigestValue</c> de la firma, base64).</param>
    /// <param name="xml">XML firmado.</param>
    public CfeEmitido(Cfe documento, Cae cae, long numero, Totales totales, DateTimeOffset fechaFirma, string codigoSeguridad, string xml)
    {
        ArgumentNullException.ThrowIfNull(documento);
        ArgumentNullException.ThrowIfNull(cae);
        ArgumentNullException.ThrowIfNull(totales);
        if (documento.FechaEmision is null || documento.Emisor is null)
            throw new ArgumentException("El documento emitido debe tener FechaEmision y Emisor.", nameof(documento));

        Documento = documento;
        Cae = cae;
        Numero = numero;
        Totales = totales;
        FechaFirma = fechaFirma;
        CodigoSeguridad = codigoSeguridad;
        Xml = xml;
    }

    /// <summary>Contenido emitido, con la fecha de emisión y el emisor efectivos.</summary>
    public Cfe Documento { get; }

    /// <summary>CAE con el que se numeró.</summary>
    public Cae Cae { get; }

    /// <summary>Tipo de comprobante.</summary>
    public TipoCfe Tipo => Documento.Tipo;

    /// <summary>Serie.</summary>
    public string Serie => Cae.Serie;

    /// <summary>Número.</summary>
    public long Numero { get; }

    /// <summary>Fecha de emisión.</summary>
    public DateOnly FechaEmision => Documento.FechaEmision!.Value;

    /// <summary>Emisor informado en el comprobante.</summary>
    public Emisor Emisor => Documento.Emisor!;

    /// <summary>Totales informados en el XML.</summary>
    public Totales Totales { get; }

    /// <summary>Fecha y hora de la firma (<c>TmstFirma</c>).</summary>
    public DateTimeOffset FechaFirma { get; }

    /// <summary>
    /// Código de seguridad: hash (<c>DigestValue</c>, base64) de la firma. Los primeros 6 caracteres
    /// se imprimen bajo el QR.
    /// </summary>
    public string CodigoSeguridad { get; }

    /// <summary>XML firmado (el que se envía a la DGI y debe conservarse durante el plazo legal).</summary>
    public string Xml { get; }

    /// <summary>
    /// Contenido del código QR de la representación impresa:
    /// <c>https://www.efactura.dgi.gub.uy/consultaQR/cfe?rut,tipo,serie,nro,monto,fecha,hash</c>.
    /// </summary>
    public string UrlQr => $"{UrlConsultaQr}?" + string.Join(",",
        Emisor.Rut,
        ((int)Tipo).ToString(CultureInfo.InvariantCulture),
        Serie,
        Numero.ToString(CultureInfo.InvariantCulture),
        CfeFormat.DecimalInvariant(MontoQr, "F2"),
        FechaFirma.ToOffset(HoraUruguay.Offset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        Uri.EscapeDataString(CodigoSeguridad));

    /// <summary>Monto del QR: e-Remito 0, e-Resguardo el total retenido, el resto el total.</summary>
    private decimal MontoQr => Tipo.Familia() switch
    {
        FamiliaCfe.Remito => 0m,
        FamiliaCfe.Resguardo => Totales.MontoRetenido,
        _ => Totales.MontoTotal,
    };

    internal const string UrlConsultaQr = "https://www.efactura.dgi.gub.uy/consultaQR/cfe";

    // --- Estado ante la DGI ---

    /// <summary>Número asignado por la DGI al sobre en que se envió (para consultar el resultado).</summary>
    public string? IdReceptorDgi { get; set; }

    /// <summary>Token del sobre en que se envió (para consultar el resultado).</summary>
    public string? TokenDgi { get; set; }

    /// <summary>
    /// Último estado informado por la DGI para el comprobante: <c>AE</c> aceptado, <c>BE</c> rechazado,
    /// <c>CE</c> observado, <c>FE</c> reliquidado; <c>null</c> mientras no haya resultado.
    /// </summary>
    public string? EstadoDgi { get; set; }

    /// <summary>Motivos o mensaje del último estado.</summary>
    public string? MensajeDgi { get; set; }

    /// <summary>Indica si la DGI aceptó el comprobante (<c>AE</c>).</summary>
    public bool AceptadoPorDgi => EstadoDgi == "AE";

    /// <summary>Indica si ya se envió en un sobre recibido por la DGI.</summary>
    public bool Enviado => !string.IsNullOrEmpty(IdReceptorDgi);

    /// <inheritdoc />
    public override string ToString() => $"{Tipo} {Serie}-{Numero}";
}
