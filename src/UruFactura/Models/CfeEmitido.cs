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

    /// <summary>
    /// Fecha del reporte diario que informa el CFE: la de Uruguay de su firma (<c>TmstFirma</c>), que puede
    /// diferir de <see cref="FechaEmision"/>. Es la fecha por la que un almacenamiento devuelve los CFE en
    /// <see cref="IUruFacturaStore.ListarCfesAsync"/>.
    /// </summary>
    public DateOnly FechaReporte => DateOnly.FromDateTime(FechaFirma.ToOffset(HoraUruguay.Offset).DateTime);

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

    /// <summary>
    /// Monto del QR: e-Remito 0, e-Resguardo el total retenido (<c>MntTotRetenido</c>), e-Remito de
    /// exportación el total (<c>MntTotal</c>) y el resto el total a pagar (<c>MntPagar</c>).
    /// </summary>
    private decimal MontoQr => Tipo.Familia() switch
    {
        FamiliaCfe.Remito => 0m,
        FamiliaCfe.Resguardo => Totales.MontoRetenido,
        FamiliaCfe.RemitoExportacion => Totales.MontoTotal,
        _ => Totales.MontoPagar,
    };

    internal const string UrlConsultaQr = "https://www.efactura.dgi.gub.uy/consultaQR/cfe";

    // --- Estado ante la DGI ---
    // Lo actualiza el cliente al enviar y consultar; un almacenamiento lo restaura al reconstruir el
    // CFE (inicializadores). Desde afuera no se puede cambiar después: el PDF y el reporte confían en él.

    private string? _idReceptorDgi;
    private string? _tokenDgi;
    private EstadoCfe? _estadoDgi;
    private string? _mensajeDgi;

    /// <summary>Número asignado por la DGI al sobre en que se envió (para consultar el resultado).</summary>
    public string? IdReceptorDgi { get => _idReceptorDgi; init => _idReceptorDgi = value; }

    /// <summary>Token del sobre en que se envió (para consultar el resultado).</summary>
    public string? TokenDgi { get => _tokenDgi; init => _tokenDgi = value; }

    /// <summary>Último estado informado por la DGI para el comprobante; <c>null</c> mientras no haya resultado.</summary>
    public EstadoCfe? EstadoDgi { get => _estadoDgi; init => _estadoDgi = value; }

    /// <summary>Motivos del último estado, o el mensaje del rechazo del sobre.</summary>
    public string? MensajeDgi { get => _mensajeDgi; init => _mensajeDgi = value; }

    /// <summary>Indica si la DGI aceptó el comprobante (<c>AE</c>).</summary>
    public bool AceptadoPorDgi => EstadoDgi == EstadoCfe.Aceptado;

    /// <summary>Indica si ya se envió en un sobre recibido por la DGI.</summary>
    public bool Enviado => !string.IsNullOrEmpty(IdReceptorDgi);

    /// <summary>
    /// Indica si la DGI lo rechazó y por lo tanto su número quedó anulado: así lo informa el reporte diario y
    /// se debe comunicar al receptor electrónico. No se anula el rechazado por E02 (ya existe en los registros
    /// de la DGI: un reenvío de un CFE recibido) ni por E09 (no corresponde al CAE).
    /// </summary>
    public bool AnuladoPorRechazo => SituacionEnReporte == SituacionReporte.Anulado;

    /// <summary>Cómo lo informa el reporte diario según su estado ante la DGI.</summary>
    internal SituacionReporte SituacionEnReporte => EstadoDgi switch
    {
        // E09: el CFE no corresponde al CAE, así que su número no es uno utilizado de ese CAE.
        EstadoCfe.Rechazado when TieneMotivo("E09") => SituacionReporte.Excluido,
        EstadoCfe.Rechazado when TieneMotivo("E02") => SituacionReporte.Emitido,
        EstadoCfe.Rechazado => SituacionReporte.Anulado,
        EstadoCfe.Observado => SituacionReporte.Observado,
        _ => SituacionReporte.Emitido,
    };

    /// <summary>Si alguno de los motivos del último estado (<c>"E02 glosa; E05 glosa"</c>) tiene el código indicado.</summary>
    private bool TieneMotivo(string codigo) =>
        (MensajeDgi ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(m => m == codigo || m.StartsWith(codigo + " ", StringComparison.Ordinal));

    /// <summary>Registra el acuse del sobre en que se envió.</summary>
    internal void RegistrarEnvio(AcuseSobre acuse)
    {
        if (acuse.Recibido)
        {
            _idReceptorDgi = acuse.IdReceptor;
            _tokenDgi = acuse.Token;
            _mensajeDgi = null;
        }
        else
            _mensajeDgi = acuse.Mensaje;
    }

    /// <summary>Registra el estado informado por la DGI.</summary>
    internal void RegistrarEstado(EstadoComprobante estado)
    {
        _estadoDgi = estado.Estado;
        _mensajeDgi = estado.Motivos.Count > 0 ? string.Join("; ", estado.Motivos) : null;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Tipo} {Serie}-{Numero}";
}

/// <summary>Cómo informa el reporte diario un CFE según su estado ante la DGI.</summary>
internal enum SituacionReporte
{
    /// <summary>Emitido (sin resultado, aceptado, reliquidado o rechazado por E02): suma montos.</summary>
    Emitido,

    /// <summary>Rechazado por la DGI: su número se informa como utilizado y anulado, sin montos.</summary>
    Anulado,

    /// <summary>CFC observado por la DGI: no se informa hasta que se corrija.</summary>
    Observado,

    /// <summary>Rechazado por E09 (no corresponde al CAE): no se informa.</summary>
    Excluido,
}
