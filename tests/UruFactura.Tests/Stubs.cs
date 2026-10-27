using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace UruFactura.Tests;

/// <summary>
/// DGI simulada (sin red): registra lo que recibe, numera los sobres y responde con los estados
/// configurados. Es thread-safe.
/// </summary>
internal sealed class DgiStub : IDgiClient
{
    private int _idReceptor;

    /// <summary>Estado del acuse de los sobres (recibido por defecto).</summary>
    public EstadoSobre EstadoSobre { get; set; } = EstadoSobre.Recibido;

    /// <summary>Estado de cada comprobante en la consulta del envío (por número); los no indicados son aceptados.</summary>
    public ConcurrentDictionary<long, EstadoCfe> EstadoPorNumero { get; } = [];

    /// <summary>Si es <c>true</c>, la consulta del envío responde que el sobre aún no se procesó.</summary>
    public bool SinProcesar { get; set; }

    /// <summary>
    /// Si es <c>true</c>, el resultado del envío trae solo <c>Nro_ordinal</c> (tipo, serie y número son
    /// opcionales en el XSD) y en orden inverso al del sobre.
    /// </summary>
    public bool SoloOrdinal { get; set; }

    public ConcurrentQueue<string> Sobres { get; } = [];
    public ConcurrentQueue<string> Reportes { get; } = [];
    public ConcurrentBag<long> IdsEnvio { get; } = [];
    private readonly ConcurrentDictionary<string, IReadOnlyList<(int Tipo, string Serie, long Numero)>> _sobres = [];

    public string? UltimoSobre => Sobres.LastOrDefault();
    public string? UltimoReporte => Reportes.LastOrDefault();

    private static long Numero(string xml, string elemento) =>
        long.Parse(Regex.Match(xml, $"<{elemento}>(\\d+)</{elemento}>").Groups[1].Value);

    public Task<AcuseSobre> EnviarSobreAsync(string sobreXml, CancellationToken cancellationToken = default)
    {
        Sobres.Enqueue(sobreXml);
        IdsEnvio.Add(Numero(sobreXml, "Idemisor"));
        var id = Interlocked.Increment(ref _idReceptor).ToString();
        _sobres[id] = [.. Regex.Matches(sobreXml, "<TipoCFE>(\\d+)</TipoCFE><Serie>(\\w+)</Serie><Nro>(\\d+)</Nro>")
            .Select(m => (int.Parse(m.Groups[1].Value), m.Groups[2].Value, long.Parse(m.Groups[3].Value)))];

        var recibido = EstadoSobre == EstadoSobre.Recibido;
        return Task.FromResult(new AcuseSobre
        {
            Estado = EstadoSobre,
            Mensaje = recibido ? "Recibido" : "Rechazado",
            IdReceptor = recibido ? id : null,
            Token = recibido ? "tok" : null,
        });
    }

    public Task<EstadoEnvio> ConsultarEnvioAsync(string idReceptor, string token, CancellationToken cancellationToken = default)
    {
        var detalle = _sobres[idReceptor].Select((c, i) =>
        {
            var estado = EstadoPorNumero.GetValueOrDefault(c.Numero, EstadoCfe.Aceptado);
            return new EstadoComprobante
            {
                NroOrdinal = i + 1,
                TipoCfe = SoloOrdinal ? null : c.Tipo,
                Serie = SoloOrdinal ? null : c.Serie,
                Numero = SoloOrdinal ? null : c.Numero,
                Estado = estado,
                Motivos = estado == EstadoCfe.Aceptado ? [] : ["E01 Fuera de rango"],
            };
        }).ToList();
        if (SoloOrdinal)
            detalle.Reverse();

        return Task.FromResult(new EstadoEnvio { Comprobantes = SinProcesar ? [] : detalle });
    }

    public Task<EstadoComprobante> ConsultarCfeAsync(TipoCfe tipo, string serie, long numero, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EstadoComprobante
        {
            TipoCfe = (int)tipo, Serie = serie, Numero = numero, Estado = EstadoPorNumero.GetValueOrDefault(numero, EstadoCfe.Aceptado),
        });

    /// <summary>Emisores del padrón simulado.</summary>
    public ConcurrentBag<EmisorElectronico> Emisores { get; } = [];

    /// <summary>Mensajes enviados a servicios de intercambio de otros emisores.</summary>
    public ConcurrentQueue<(string Url, OperacionIntercambio Operacion, string Xml)> Intercambios { get; } = [];

    /// <summary>Respuesta del servicio de intercambio del otro emisor.</summary>
    public RespuestaIntercambio RespuestaIntercambio { get; set; } = new(1000, "Sobre Recibido.", null);

    public Task<IReadOnlyList<EmisorElectronico>> ConsultarEmisoresAsync(string filtroRut, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmisorElectronico>>([.. Emisores.Where(e => e.Rut.Contains(filtroRut, StringComparison.Ordinal))]);

    public Task<RespuestaIntercambio> EnviarIntercambioAsync(
        string url, OperacionIntercambio operacion, string xml, CancellationToken cancellationToken = default)
    {
        Intercambios.Enqueue((url, operacion, xml));
        return Task.FromResult(RespuestaIntercambio);
    }

    public Task<AcuseReporte> EnviarReporteAsync(string reporteFirmadoXml, CancellationToken cancellationToken = default)
    {
        Reportes.Enqueue(reporteFirmadoXml);
        IdsEnvio.Add(Numero(reporteFirmadoXml, "IDEmisor"));
        return Task.FromResult(new AcuseReporte { Estado = EstadoReporte.Recibido, Mensaje = "Recibido" });
    }

    /// <summary>
    /// Un cliente con esta DGI simulada, el certificado de prueba y el almacenamiento indicado (o uno en
    /// memoria), con el reloj en <see cref="TestData.Dia"/> salvo que se indique otro.
    /// </summary>
    public UruFacturaClient Cliente(IUruFacturaStore? store = null, TimeProvider? reloj = null, UruFacturaConfig? config = null) =>
        new(config ?? TestData.Config(), store, dgiClient: this, reloj: reloj ?? TestData.Reloj);
}
