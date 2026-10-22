using UruFactura.Enums;
using UruFactura.Models;

namespace UruFactura.EntityFrameworkCore;

/// <summary>
/// CFE emitido (tabla <c>UruFacturaCfes</c>): numeración, totales para el reporte diario, XML firmado
/// y estado ante la DGI. Tipo, serie y número son únicos.
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
    public decimal? TipoCambio { get; set; }

    public decimal MontoNetoExento { get; set; }
    public decimal MontoNetoSuspendido { get; set; }
    public decimal MontoNetoMinimo { get; set; }
    public decimal MontoNetoBasico { get; set; }
    public decimal IvaMinimo { get; set; }
    public decimal IvaBasico { get; set; }
    public decimal MontoExportacion { get; set; }
    public decimal MontoTotal { get; set; }
    public decimal MontoRetenido { get; set; }

    public DateTimeOffset? FechaFirma { get; set; }
    public string? CodigoSeguridad { get; set; }

    /// <summary>XML firmado tal como se envió a la DGI (debe conservarse durante el plazo legal).</summary>
    public string? XmlFirmado { get; set; }

    public string? IdReceptorDgi { get; set; }
    public string? TokenDgi { get; set; }
    public string? CodigoRespuestaDgi { get; set; }
    public string? MensajeRespuestaDgi { get; set; }
    public bool AceptadoPorDgi { get; set; }

    /// <summary>Alta del registro (UTC).</summary>
    public DateTimeOffset FechaRegistro { get; set; }

    /// <summary>Última actualización del registro (UTC).</summary>
    public DateTimeOffset FechaActualizacion { get; set; }

    /// <summary>
    /// Reconstruye el CFE con lo necesario para el reporte diario, para consultar su estado y para
    /// reenviarlo (<see cref="Cfe.XmlFirmado"/>). No incluye el detalle ni los datos del receptor.
    /// </summary>
    public Cfe ToCfe() => new()
    {
        Tipo = TipoCfe,
        Serie = Serie,
        Numero = Numero,
        FechaEmision = FechaEmision,
        CodigoSucursal = CodigoSucursal,
        NroInterno = NroInterno,
        Moneda = Moneda,
        TipoCambio = TipoCambio,
        MontoNetoExento = MontoNetoExento,
        MontoNetoSuspendido = MontoNetoSuspendido,
        MontoNetoMinimo = MontoNetoMinimo,
        MontoNetoBasico = MontoNetoBasico,
        IvaMinimo = IvaMinimo,
        IvaBasico = IvaBasico,
        MontoExportacion = MontoExportacion,
        MontoTotal = MontoTotal,
        MontoRetenido = MontoRetenido,
        FechaFirma = FechaFirma,
        CodigoSeguridad = CodigoSeguridad,
        XmlFirmado = XmlFirmado,
        IdReceptorDgi = IdReceptorDgi,
        TokenDgi = TokenDgi,
        CodigoRespuestaDgi = CodigoRespuestaDgi,
        MensajeRespuestaDgi = MensajeRespuestaDgi,
        AceptadoPorDgi = AceptadoPorDgi,
    };

    /// <summary>
    /// Copia los datos del CFE. Los opcionales que lleguen vacíos (p. ej. el XML de un CFE
    /// reconstruido sin él) no borran los guardados.
    /// </summary>
    internal void CopiarDesde(Cfe cfe, DateTimeOffset ahora)
    {
        FechaEmision = cfe.FechaEmision;
        CodigoSucursal = cfe.CodigoSucursal;
        NroInterno = cfe.NroInterno ?? NroInterno;
        ReceptorDocumento = string.IsNullOrWhiteSpace(cfe.Receptor?.Documento) ? ReceptorDocumento : cfe.Receptor.Documento;
        Moneda = cfe.Moneda;
        TipoCambio = cfe.TipoCambio;

        MontoNetoExento = cfe.MontoNetoExento;
        MontoNetoSuspendido = cfe.MontoNetoSuspendido;
        MontoNetoMinimo = cfe.MontoNetoMinimo;
        MontoNetoBasico = cfe.MontoNetoBasico;
        IvaMinimo = cfe.IvaMinimo;
        IvaBasico = cfe.IvaBasico;
        MontoExportacion = cfe.MontoExportacion;
        MontoTotal = cfe.MontoTotal;
        MontoRetenido = cfe.MontoRetenido;

        // En UTC: Npgsql solo admite offset 0 en "timestamp with time zone" y TmstFirma viene en −03:00.
        FechaFirma = cfe.FechaFirma?.ToUniversalTime() ?? FechaFirma;
        CodigoSeguridad = cfe.CodigoSeguridad ?? CodigoSeguridad;
        XmlFirmado = string.IsNullOrWhiteSpace(cfe.XmlFirmado) ? XmlFirmado : cfe.XmlFirmado;
        IdReceptorDgi = cfe.IdReceptorDgi ?? IdReceptorDgi;
        TokenDgi = cfe.TokenDgi ?? TokenDgi;
        CodigoRespuestaDgi = cfe.CodigoRespuestaDgi ?? CodigoRespuestaDgi;
        MensajeRespuestaDgi = cfe.MensajeRespuestaDgi ?? MensajeRespuestaDgi;
        AceptadoPorDgi = cfe.AceptadoPorDgi;
        FechaActualizacion = ahora;
    }
}
