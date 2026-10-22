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

    /// <summary>Estado del acuse de los sobres (<c>AS</c> por defecto).</summary>
    public string EstadoSobre { get; set; } = "AS";

    /// <summary>Estado de cada comprobante en la consulta del envío (por número); los no indicados son <c>AE</c>.</summary>
    public ConcurrentDictionary<long, string> EstadoPorNumero { get; } = [];

    /// <summary>Si es <c>true</c>, la consulta del envío responde que el sobre aún no se procesó.</summary>
    public bool SinProcesar { get; set; }

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

        return Task.FromResult(new AcuseSobre
        {
            Estado = EstadoSobre,
            Mensaje = EstadoSobre == "AS" ? "Recibido" : "Rechazado",
            IdReceptor = EstadoSobre == "AS" ? id : null,
            Token = EstadoSobre == "AS" ? "tok" : null,
        });
    }

    public Task<EstadoEnvio> ConsultarEnvioAsync(string idReceptor, string token, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EstadoEnvio
        {
            Comprobantes = SinProcesar ? [] : [.. _sobres[idReceptor].Select(c => new EstadoComprobante
            {
                TipoCfe = c.Tipo,
                Serie = c.Serie,
                Numero = c.Numero,
                Estado = EstadoPorNumero.GetValueOrDefault(c.Numero, "AE"),
                Motivos = EstadoPorNumero.GetValueOrDefault(c.Numero, "AE") == "AE" ? [] : ["E01 Fuera de rango"],
            })],
        });

    public Task<EstadoComprobante> ConsultarCfeAsync(TipoCfe tipo, string serie, long numero, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EstadoComprobante
        {
            TipoCfe = (int)tipo, Serie = serie, Numero = numero, Estado = EstadoPorNumero.GetValueOrDefault(numero, "AE"),
        });

    public Task<AcuseReporte> EnviarReporteAsync(string reporteFirmadoXml, CancellationToken cancellationToken = default)
    {
        Reportes.Enqueue(reporteFirmadoXml);
        IdsEnvio.Add(Numero(reporteFirmadoXml, "IDEmisor"));
        return Task.FromResult(new AcuseReporte { Estado = "AR", Mensaje = "Recibido" });
    }

    /// <summary>Un cliente con esta DGI simulada, el certificado de prueba y el almacenamiento indicado.</summary>
    public UruFacturaClient Cliente(IUruFacturaStore? store = null, TimeProvider? reloj = null, UruFacturaConfig? config = null) =>
        new(config ?? TestData.Config(), store, dgiClient: this, reloj: reloj);
}
