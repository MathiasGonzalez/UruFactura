using System.Text.Json;

namespace UruFactura.EntityFrameworkCore;

/// <summary>
/// CFE emitido (tabla <c>UruFacturaCfes</c>): el XML firmado, el contenido (en <see cref="Datos"/>,
/// JSON) y el estado ante la DGI, con columnas para buscar. Tipo, serie y número son únicos.
/// </summary>
public class CfeEntity
{
    /// <summary>Clave ordenada por tiempo (UUID v7; secuencial en SQL Server), generada al agregar la entidad.</summary>
    public Guid Id { get; set; }

    public TipoCfe TipoCfe { get; set; }
    public string Serie { get; set; } = string.Empty;
    public long Numero { get; set; }
    public DateOnly FechaEmision { get; set; }
    public int CodigoSucursal { get; set; }

    /// <summary>Número interno del emisor (p. ej. el id del pedido o del pago), para búsquedas e idempotencia.</summary>
    public string? NroInterno { get; set; }

    /// <summary>Documento del receptor, si se informó.</summary>
    public string? ReceptorDocumento { get; set; }

    public Moneda Moneda { get; set; }
    public decimal MontoTotal { get; set; }

    /// <summary>Fecha y hora de la firma, en UTC.</summary>
    public DateTimeOffset FechaFirma { get; set; }

    public string CodigoSeguridad { get; set; } = string.Empty;

    /// <summary>XML firmado tal como se envía a la DGI (debe conservarse durante el plazo legal).</summary>
    public string Xml { get; set; } = string.Empty;

    /// <summary>Contenido emitido, CAE y totales en JSON (para reconstruir el <see cref="CfeEmitido"/>).</summary>
    public string Datos { get; set; } = string.Empty;

    public string? IdReceptorDgi { get; set; }
    public string? TokenDgi { get; set; }

    /// <summary>Último estado informado por la DGI (se guarda como texto: <c>Aceptado</c>, <c>Rechazado</c>, ...); <c>null</c> sin resultado.</summary>
    public EstadoCfe? EstadoDgi { get; set; }

    public string? MensajeDgi { get; set; }

    /// <summary>Alta del registro (UTC).</summary>
    public DateTimeOffset FechaRegistro { get; set; }

    /// <summary>Última actualización del registro (UTC).</summary>
    public DateTimeOffset FechaActualizacion { get; set; }

    /// <summary>Lo que se guarda en <see cref="Datos"/>.</summary>
    private sealed record DatosCfe(Cfe Documento, Cae Cae, Totales Totales);

    /// <summary>Reconstruye el CFE emitido, con su estado ante la DGI.</summary>
    public CfeEmitido ToCfeEmitido()
    {
        var datos = JsonSerializer.Deserialize<DatosCfe>(Datos)
            ?? throw new InvalidOperationException($"El CFE {TipoCfe} {Serie}-{Numero} no tiene datos.");

        return new CfeEmitido(datos.Documento, datos.Cae, Numero, datos.Totales, FechaFirma, CodigoSeguridad, Xml)
        {
            IdReceptorDgi = IdReceptorDgi,
            TokenDgi = TokenDgi,
            EstadoDgi = EstadoDgi,
            MensajeDgi = MensajeDgi,
        };
    }

    internal static CfeEntity Desde(CfeEmitido cfe, DateTimeOffset ahora)
    {
        var entidad = new CfeEntity
        {
            TipoCfe = cfe.Tipo,
            Serie = cfe.Serie,
            Numero = cfe.Numero,
            FechaEmision = cfe.FechaEmision,
            CodigoSucursal = cfe.Emisor.CodigoSucursal,
            NroInterno = cfe.Documento.NroInterno,
            ReceptorDocumento = cfe.Documento.Receptor?.Documento,
            Moneda = cfe.Documento.Moneda,
            MontoTotal = cfe.Totales.MontoTotal,
            // En UTC: Npgsql solo admite offset 0 en "timestamp with time zone" y TmstFirma viene en −03:00.
            FechaFirma = cfe.FechaFirma.ToUniversalTime(),
            CodigoSeguridad = cfe.CodigoSeguridad,
            Xml = cfe.Xml,
            Datos = JsonSerializer.Serialize(new DatosCfe(cfe.Documento, cfe.Cae, cfe.Totales)),
            FechaRegistro = ahora,
        };
        entidad.CopiarEstado(cfe, ahora);
        return entidad;
    }

    internal void CopiarEstado(CfeEmitido cfe, DateTimeOffset ahora)
    {
        IdReceptorDgi = cfe.IdReceptorDgi;
        TokenDgi = cfe.TokenDgi;
        EstadoDgi = cfe.EstadoDgi;
        MensajeDgi = cfe.MensajeDgi;
        FechaActualizacion = ahora;
    }
}
