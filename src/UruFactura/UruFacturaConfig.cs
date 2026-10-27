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
    /// CA no es de confianza; en Producción la configuración no es válida.
    /// </summary>
    public bool OmitirValidacionSsl { get; init; }

    /// <summary>
    /// Valor en pesos de la Unidad Indexada al cierre de cada año civil: la clave es el año y el valor, la UI del
    /// 31/12 de ese año (por ejemplo, <c>[2025] = 6.4m</c> para los comprobantes de 2026). Fija el tope de 5.000 UI
    /// de los e-Tickets, que se toma de la UI al cierre del año anterior a la fecha de cada comprobante: por encima
    /// hay que identificar al receptor y enviarlos a la DGI uno a uno, y el reporte diario informa cuántos lo
    /// superaron (<c>CantDocsMay_topeUI</c>). Obligatorio en Producción, donde falta el año que se necesita es un
    /// error al emitir.
    /// </summary>
    public IReadOnlyDictionary<int, decimal> UnidadIndexada { get; init; } = new Dictionary<int, decimal>();

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
        var errores = Emisor.Validar().Select(e => e.ToString()).ToList();

        if (Certificado is null && string.IsNullOrWhiteSpace(RutaCertificado))
            errores.Add("Se requiere el certificado: Certificado o RutaCertificado.");
        if (Ambiente == Ambiente.Produccion && (string.IsNullOrWhiteSpace(UrlEnvio) || string.IsNullOrWhiteSpace(UrlConsultas)))
            errores.Add("En Producción UrlEnvio y UrlConsultas son obligatorias (las entrega la DGI al habilitar el ambiente).");
        if (Ambiente == Ambiente.Produccion && OmitirValidacionSsl)
            errores.Add("OmitirValidacionSsl no se admite en Producción: la conexión con la DGI debe validar su certificado.");
        if (Timeout <= TimeSpan.Zero)
            errores.Add("Timeout debe ser mayor a cero.");
        if (UnidadIndexada is null)
            errores.Add("UnidadIndexada no puede ser null (puede estar vacía en Homologación).");
        else
        {
            foreach (var (anio, ui) in UnidadIndexada)
            {
                if (anio is < 2011 or > 2050)
                    errores.Add($"UnidadIndexada: el año {anio} está fuera del rango de los CFE (2011-2050).");
                if (ui <= 0)
                    errores.Add($"UnidadIndexada: el valor del {anio} debe ser mayor a cero.");
            }
            if (UnidadIndexada.Count == 0 && Ambiente == Ambiente.Produccion)
                errores.Add("En Producción UnidadIndexada es obligatoria (la UI al cierre de cada año): fija el tope de los e-Tickets.");
        }

        return errores;
    }

    /// <summary>
    /// Tope en pesos (5.000 UI) para un comprobante de <paramref name="fechaComprobante"/>: con la UI al cierre del
    /// año anterior. <c>null</c> si falta ese año en Homologación.
    /// </summary>
    /// <exception cref="UruFacturaException">Si falta ese año en Producción.</exception>
    internal decimal? TopeMontoNeto(DateOnly fechaComprobante)
    {
        // Una fecha fuera del rango de los CFE es un error de validación del comprobante, no de la configuración.
        if (fechaComprobante < Validacion.FechaMinima || fechaComprobante > Validacion.FechaMaxima)
            return null;
        var anio = fechaComprobante.Year - 1;
        if (UnidadIndexada.TryGetValue(anio, out var ui))
            return ui * 5000m;
        if (Ambiente == Ambiente.Produccion)
            throw new UruFacturaException(
                $"Falta en UnidadIndexada el valor de la UI al 31/12/{anio}: fija el tope de 5.000 UI de los comprobantes de {anio + 1}.");
        return null;
    }
}
