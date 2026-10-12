using UruFactura.Enums;

namespace UruFactura.Configuration;

/// <summary>
/// Configuración principal de UruFactura.
/// </summary>
public record UruFacturaConfig
{
    /// <summary>RUT de la empresa emisora (sin puntos ni guión, 12 dígitos).</summary>
    public string RutEmisor { get; init; } = string.Empty;

    /// <summary>Razón social del emisor.</summary>
    public string RazonSocialEmisor { get; init; } = string.Empty;

    /// <summary>Nombre comercial del emisor (opcional).</summary>
    public string? NombreComercialEmisor { get; init; }

    /// <summary>Giro o actividad económica del emisor (opcional).</summary>
    public string? Giro { get; init; }

    /// <summary>Domicilio fiscal del emisor.</summary>
    public string DomicilioFiscal { get; init; } = string.Empty;

    /// <summary>Ciudad del emisor.</summary>
    public string Ciudad { get; init; } = string.Empty;

    /// <summary>Departamento del emisor.</summary>
    public string Departamento { get; init; } = string.Empty;

    /// <summary>Ambiente de operación (Homologación o Producción).</summary>
    public Ambiente Ambiente { get; init; } = Ambiente.Homologacion;

    /// <summary>Ruta al certificado digital .p12/.pfx.</summary>
    public string RutaCertificado { get; init; } = string.Empty;

    /// <summary>Contraseña del certificado digital.</summary>
    public string PasswordCertificado { get; init; } = string.Empty;

    /// <summary>Timeout en segundos para las llamadas SOAP (por defecto 30 s).</summary>
    public int SoapTimeoutSegundos { get; init; } = 30;

    /// <summary>
    /// Cuando es <c>true</c>, deshabilita la validación del certificado TLS del servidor.
    /// Útil para pruebas contra el ambiente de Homologación cuando la CA no es de confianza.
    /// <b>No habilitar en Producción.</b>
    /// Por defecto es <c>false</c>; el SDK no desactiva la validación automáticamente.
    /// </summary>
    public bool OmitirValidacionSsl { get; init; }

    /// <summary>
    /// Código de sucursal asignado por la DGI (<c>CdgDGISucur</c>, hasta 4 dígitos).
    /// Para casa central suele ser 1.
    /// </summary>
    public int CodigoSucursal { get; init; } = 1;

    /// <summary>Algoritmo de firma del CFE y de WS-Security (por defecto SHA-256).</summary>
    public AlgoritmoFirma AlgoritmoFirma { get; init; } = AlgoritmoFirma.Sha256;

    /// <summary>
    /// URL del servicio de recepción (<c>ws_efactura</c>: sobre, reporte y estado del envío).
    /// Si se omite, en Homologación se usa el servicio de prueba de la DGI; en Producción
    /// es obligatoria (la DGI no publica una URL de producción en su documentación abierta).
    /// </summary>
    public string? UrlEnvio { get; init; }

    /// <summary>
    /// URL del servicio de consultas (<c>ws_consultas</c>: estado de un CFE).
    /// Misma regla que <see cref="UrlEnvio"/>.
    /// </summary>
    public string? UrlConsultas { get; init; }

    /// <summary>URL efectiva del servicio de recepción.</summary>
    public string DgiEnvioUrl =>
        !string.IsNullOrWhiteSpace(UrlEnvio) ? UrlEnvio
        : Ambiente == Ambiente.Homologacion ? "https://efactura.dgi.gub.uy:6443/ePrueba/ws_eprueba"
        : throw new Exceptions.UruFacturaException(
            "En Producción debe configurar UrlEnvio con la URL del servicio ws_efactura entregada por la DGI.");

    /// <summary>URL efectiva del servicio de consultas.</summary>
    public string DgiConsultasUrl =>
        !string.IsNullOrWhiteSpace(UrlConsultas) ? UrlConsultas
        : Ambiente == Ambiente.Homologacion ? "https://efactura.dgi.gub.uy:6460/ePrueba/ws_consultasPrueba"
        : throw new Exceptions.UruFacturaException(
            "En Producción debe configurar UrlConsultas con la URL del servicio ws_consultas entregada por la DGI.");

    /// <summary>
    /// Valida que la configuración obligatoria esté presente.
    /// </summary>
    public void Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(RutEmisor))
            errors.Add("RutEmisor es obligatorio.");

        if (string.IsNullOrWhiteSpace(RazonSocialEmisor))
            errors.Add("RazonSocialEmisor es obligatorio.");

        if (string.IsNullOrWhiteSpace(DomicilioFiscal))
            errors.Add("DomicilioFiscal es obligatorio.");

        if (string.IsNullOrWhiteSpace(Ciudad))
            errors.Add("Ciudad es obligatoria.");

        if (string.IsNullOrWhiteSpace(Departamento))
            errors.Add("Departamento es obligatorio.");

        if (string.IsNullOrWhiteSpace(RutaCertificado))
            errors.Add("RutaCertificado es obligatorio.");

        if (string.IsNullOrWhiteSpace(PasswordCertificado))
            errors.Add("PasswordCertificado es obligatorio.");

        if (CodigoSucursal is < 1 or > 9999)
            errors.Add("CodigoSucursal debe estar entre 1 y 9999.");

        if (Ambiente == Ambiente.Produccion
            && (string.IsNullOrWhiteSpace(UrlEnvio) || string.IsNullOrWhiteSpace(UrlConsultas)))
            errors.Add("En Producción UrlEnvio y UrlConsultas son obligatorias.");

        if (errors.Count > 0)
            throw new Exceptions.UruFacturaException(
                $"Configuración inválida: {string.Join("; ", errors)}");
    }
}
