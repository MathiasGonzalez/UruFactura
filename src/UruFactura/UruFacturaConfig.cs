using System.Security.Cryptography.X509Certificates;

namespace UruFactura;

/// <summary>
/// Configuración de UruFactura. Es inmutable: se valida al construir el <see cref="UruFacturaClient"/>.
/// </summary>
public sealed record UruFacturaConfig
{
    /// <summary>Datos del emisor que se informan en cada CFE.</summary>
    public Emisor Emisor { get; init; } = new();

    /// <summary>Ambiente de la DGI (por defecto, Homologación).</summary>
    public Ambiente Ambiente { get; init; } = Ambiente.Homologacion;

    /// <summary>Ruta del certificado .p12/.pfx. Alternativa a <see cref="Certificado"/>.</summary>
    public string? RutaCertificado { get; init; }

    /// <summary>Contraseña del archivo <see cref="RutaCertificado"/>.</summary>
    public string? PasswordCertificado { get; init; }

    /// <summary>
    /// Certificado con clave privada, ya cargado (por ejemplo, desde Azure Key Vault o un HSM).
    /// Tiene prioridad sobre <see cref="RutaCertificado"/>. El cliente no lo libera: es del llamador.
    /// </summary>
    public X509Certificate2? Certificado { get; init; }

    /// <summary>Algoritmo de la firma del CFE y de WS-Security (por defecto SHA-256).</summary>
    public AlgoritmoFirma AlgoritmoFirma { get; init; } = AlgoritmoFirma.Sha256;

    /// <summary>
    /// URL del servicio de recepción (<c>ws_efactura</c>: sobres, reporte diario y estado del envío).
    /// En Homologación, si se omite, se usa el servicio de prueba de la DGI; en Producción es obligatoria.
    /// </summary>
    public string? UrlEnvio { get; init; }

    /// <summary>URL del servicio de consultas (<c>ws_consultas</c>). Misma regla que <see cref="UrlEnvio"/>.</summary>
    public string? UrlConsultas { get; init; }

    /// <summary>Timeout de las llamadas a la DGI (por defecto 30 segundos).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Deshabilita la validación del certificado TLS del servidor. Solo para Homologación cuando la
    /// CA no es de confianza. <b>Nunca en Producción.</b>
    /// </summary>
    public bool OmitirValidacionSsl { get; init; }

    /// <summary>
    /// Valor de la Unidad Indexada en pesos. Si se informa, los e-Tickets con monto neto superior a
    /// 5.000 UI deben identificar al receptor (obligación del formato CFE).
    /// </summary>
    public decimal? ValorUnidadIndexada { get; init; }

    internal const string UrlEnvioHomologacion = "https://efactura.dgi.gub.uy:6443/ePrueba/ws_eprueba";
    internal const string UrlConsultasHomologacion = "https://efactura.dgi.gub.uy:6460/ePrueba/ws_consultasPrueba";

    internal string UrlEnvioEfectiva =>
        !string.IsNullOrWhiteSpace(UrlEnvio) ? UrlEnvio : UrlEnvioHomologacion;

    internal string UrlConsultasEfectiva =>
        !string.IsNullOrWhiteSpace(UrlConsultas) ? UrlConsultas : UrlConsultasHomologacion;

    /// <summary>Lo que muestra <c>ToString()</c>: sin la contraseña ni el certificado, para poder loguear la configuración.</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"Emisor = {Emisor.Rut}, Ambiente = {Ambiente}, AlgoritmoFirma = {AlgoritmoFirma}, ");
        builder.Append($"Certificado = {(Certificado is not null ? "(cargado)" : RutaCertificado)}, ");
        builder.Append($"UrlEnvio = {UrlEnvioEfectiva}, UrlConsultas = {UrlConsultasEfectiva}, Timeout = {Timeout}");
        return true;
    }

    /// <summary>Devuelve los errores de la configuración (vacía si es válida).</summary>
    public IReadOnlyList<string> Validar()
    {
        var errores = Emisor.Validar().ToList();

        if (Certificado is null && string.IsNullOrWhiteSpace(RutaCertificado))
            errores.Add("Se requiere el certificado: Certificado o RutaCertificado.");
        if (Ambiente == Ambiente.Produccion && (string.IsNullOrWhiteSpace(UrlEnvio) || string.IsNullOrWhiteSpace(UrlConsultas)))
            errores.Add("En Producción UrlEnvio y UrlConsultas son obligatorias (las entrega la DGI al habilitar el ambiente).");
        if (Timeout <= TimeSpan.Zero)
            errores.Add("Timeout debe ser mayor a cero.");
        if (ValorUnidadIndexada is <= 0)
            errores.Add("ValorUnidadIndexada debe ser mayor a cero.");

        return errores;
    }
}
