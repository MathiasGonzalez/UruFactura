using System.Globalization;
using FluentReport;
using FluentReport.Builders;
using FluentReport.Core;
using FluentReport.Styling;
using SkiaSharp;
using ZXing;
using ZXing.QrCode;
using ZXing.Rendering;

namespace UruFactura;

/// <summary>
/// Representación impresa de los CFE emitidos: PDF A4, PDF térmico (ticket de 80 mm) y el código QR,
/// con lo que exige el formato de la DGI: identificación del comprobante (RUC, tipo, serie y número, forma
/// de pago), recuadros "CONSUMO FINAL" o "RUC COMPRADOR", leyendas de los CAE especiales y, en el pie, el
/// sello digital (QR y código de seguridad), la constancia de IVA al día, el CAE y su vencimiento.
/// </summary>
public static class CfePdf
{
    private static readonly CultureInfo Cultura = new("es-UY");

    /// <summary>Genera el PDF A4 del CFE.</summary>
    /// <exception cref="PdfGenerationException">Si falla la generación.</exception>
    public static byte[] GenerarA4(CfeEmitido cfe)
    {
        ArgumentNullException.ThrowIfNull(cfe);
        try
        {
            return GenerarDocumentoA4(cfe, GenerarQr(cfe));
        }
        catch (Exception ex) when (ex is not PdfGenerationException)
        {
            throw new PdfGenerationException("Error al generar el PDF A4 del CFE.", ex);
        }
    }

    /// <summary>Genera el PDF térmico (ticket de 80 mm) del CFE.</summary>
    /// <exception cref="PdfGenerationException">Si falla la generación.</exception>
    public static byte[] GenerarTermico(CfeEmitido cfe)
    {
        ArgumentNullException.ThrowIfNull(cfe);
        try
        {
            return GenerarDocumentoTermico(cfe, GenerarQr(cfe));
        }
        catch (Exception ex) when (ex is not PdfGenerationException)
        {
            throw new PdfGenerationException("Error al generar el PDF térmico del CFE.", ex);
        }
    }

    /// <summary>Genera la imagen PNG del código QR del CFE (contenido: <see cref="CfeEmitido.UrlQr"/>).</summary>
    public static byte[] GenerarQr(CfeEmitido cfe)
    {
        ArgumentNullException.ThrowIfNull(cfe);
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = 300,
                Height = 300,
                Margin = 1,
                ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
            },
        };

        var pixelData = writer.Write(cfe.UrlQr);

        // Píxeles BGRA a PNG con SkiaSharp.
        using var bitmap = new SKBitmap(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bitmap.GetPixels(), pixelData.Pixels.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // -----------------------------------------------------------------------
    // Contenido (común a los dos formatos)
    // -----------------------------------------------------------------------

    private static string Monto(decimal valor) => valor.ToString("N2", Cultura);

    /// <summary>Cantidad con los decimales que tiene (hasta 3, como en el XML).</summary>
    private static string Cantidad(decimal valor) => valor.ToString("#,0.###", Cultura);

    /// <summary>Precio unitario con al menos 2 decimales y hasta 6, como en el XML.</summary>
    private static string Precio(decimal valor) => valor.ToString("#,0.00####", Cultura);

    private static string Porcentaje(decimal tasa) => $"{tasa.ToString("0.###", Cultura)}%";

    private static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", Cultura);

    /// <summary>
    /// Sin mención al IVA: los contribuyentes IVA mínimo y Monotributo (y las ventas por cuenta ajena de un
    /// mandante IVA mínimo) no discriminan IVA en la representación impresa.
    /// </summary>
    internal static bool SinIva(CfeEmitido e) =>
        e.Cae.Especial is CaeEspecial.IvaMinimo or CaeEspecial.Monotributo or CaeEspecial.MonotributoSocialMides
        || e.Documento.Detalle.Any(l => l.IndFact == IndicadorFacturacion.IvaMinimoMonotributo);

    /// <summary>Columna del detalle: título, ancho (relativo o fijo, en puntos) y alineación.</summary>
    internal sealed record Columna(string Titulo, float Ancho, bool AnchoFijo = false, bool Derecha = true);

    /// <summary>
    /// Columnas del detalle según el tipo: el e-Remito no lleva precios, la exportación no lleva IVA, el
    /// e-Resguardo informa retenciones/percepciones en lugar de ítems y los contribuyentes IVA mínimo o
    /// Monotributo no mencionan el IVA.
    /// </summary>
    internal static Columna[] Columnas(CfeEmitido e) => e.Tipo.Familia() switch
    {
        FamiliaCfe.Remito => [new("Cant.", 50, AnchoFijo: true), new("Descripción", 1, Derecha: false)],
        FamiliaCfe.FacturaExportacion or FamiliaCfe.RemitoExportacion or FamiliaCfe.Boleta =>
            [new("Cant.", 40, AnchoFijo: true), new("Descripción", 5, Derecha: false), new("P.Unit.", 2), new("Total", 2)],
        FamiliaCfe.Resguardo => [new("Código", 2, Derecha: false), new("Tasa", 1), new("Monto sujeto", 2), new("Valor", 2)],
        _ when SinIva(e) => [new("Cant.", 40, AnchoFijo: true), new("Descripción", 5, Derecha: false), new("P.Unit.", 2), new("Total", 2)],
        _ => [new("Cant.", 40, AnchoFijo: true), new("Descripción", 5, Derecha: false), new("P.Unit.", 2), new("IVA", 1), new("Total", 2)],
    };

    /// <summary>Filas del detalle, con las columnas de <see cref="Columnas"/>.</summary>
    internal static IEnumerable<string[]> Filas(CfeEmitido e)
    {
        var cfe = e.Documento;
        return cfe.Tipo.Familia() switch
        {
            // Los ítems a rebajar (8) y a anular (9) se imprimen con el signo en lugar del indicador.
            FamiliaCfe.Remito => cfe.Detalle.Select(l => new[] { ConSigno(l, Cantidad(l.Cantidad)), l.NombreItem }),
            FamiliaCfe.FacturaExportacion or FamiliaCfe.RemitoExportacion or FamiliaCfe.Boleta => cfe.Detalle.Select(l =>
                new[] { Cantidad(l.Cantidad), Descripcion(l), Precio(l.PrecioUnitario), MontoLinea(cfe, l) }),
            FamiliaCfe.Resguardo => cfe.Detalle.SelectMany(l => l.Retenciones.Select(r =>
                new[] { r.Codigo, r.Tasa is { } tasa ? Porcentaje(tasa) : "", Monto(r.MontoSujeto), ConSigno(l, Monto(r.Valor)) })),
            _ when SinIva(e) => cfe.Detalle.Select(l =>
                new[] { Cantidad(l.Cantidad), l.NombreItem, Precio(l.PrecioUnitario), MontoLinea(cfe, l) }),
            _ => cfe.Detalle.Select(l =>
                new[] { Cantidad(l.Cantidad), l.NombreItem, Precio(l.PrecioUnitario), EtiquetaIndicador(l.IndFact), MontoLinea(cfe, l) }),
        };
    }

    /// <summary>Nombre del ítem, con su posición arancelaria (NCM) si la tiene: es un dato que se imprime.</summary>
    private static string Descripcion(LineaDetalle linea) =>
        string.IsNullOrWhiteSpace(linea.Ncm) ? linea.NombreItem : $"{linea.NombreItem} (NCM {linea.Ncm})";

    /// <summary>
    /// Monto de la línea; el de un no facturable negativo o un ítem a rebajar se imprime con el signo (el formato lo
    /// pide en lugar del indicador).
    /// </summary>
    private static string MontoLinea(Cfe cfe, LineaDetalle linea) =>
        cfe.IndicadorInformado(linea.IndFact) == IndicadorFacturacion.NoFacturableNegativo ? $"-{Monto(linea.MontoTotal)}" : ConSigno(linea, Monto(linea.MontoTotal));

    /// <summary>El valor con el signo menos si la línea rebaja un remito (8) o anula un resguardo (9).</summary>
    private static string ConSigno(LineaDetalle linea, string valor) =>
        linea.IndFact is IndicadorFacturacion.RebajaRemito or IndicadorFacturacion.AnulacionResguardo ? $"-{valor}" : valor;

    /// <summary>Indicador de facturación de una línea de e-Ticket o e-Factura.</summary>
    internal static string EtiquetaIndicador(IndicadorFacturacion indicador) => indicador switch
    {
        IndicadorFacturacion.Exento => "Exento",
        IndicadorFacturacion.TasaMinima => Porcentaje(Totales.TasaIvaMinima),
        IndicadorFacturacion.TasaBasica => Porcentaje(Totales.TasaIvaBasica),
        IndicadorFacturacion.OtraTasa => "Otra tasa",
        IndicadorFacturacion.EntregaGratuita => "Gratuito",
        IndicadorFacturacion.NoFacturable or IndicadorFacturacion.NoFacturableNegativo => "No fact.",
        IndicadorFacturacion.ExportacionYAsimiladas => "Export.",
        IndicadorFacturacion.ImpuestoPercibido => "Percibido",
        IndicadorFacturacion.IvaEnSuspenso => "Susp.",
        _ => "",
    };

    /// <summary>Forma de pago (Contado o Crédito), en los tipos que la informan.</summary>
    internal static string? FormaDePago(Cfe cfe) =>
        cfe.Tipo.Familia() is FamiliaCfe.Ticket or FamiliaCfe.Factura or FamiliaCfe.FacturaExportacion or FamiliaCfe.Boleta
            ? cfe.FormaPago == FormaPago.Credito ? "Crédito" : "Contado"
            : null;

    /// <summary>
    /// Leyendas obligatorias de la identificación del comprobante y del cuerpo: "Bienes de Terceros" con el
    /// propietario (e-Remitos), "Pagos por cuenta de terceros", la fecha valor del e-Resguardo y el período facturado.
    /// La de cobranza propia va junto al tipo (<see cref="NombreCompleto(Cfe)"/>).
    /// </summary>
    internal static List<string> Leyendas(Cfe cfe)
    {
        var leyendas = new List<string>();
        if (cfe.Propietario is { } p)
            leyendas.Add($"Bienes de Terceros - {p.Nombre} ({DocumentoTexto(p.TipoDocumento, p.CodigoPais, p.Documento)})");
        if (cfe.PagoCuentaTerceros)
            leyendas.Add("Pagos por cuenta de terceros");
        if (cfe.FechaValor is { } fechaValor)
            leyendas.Add($"Fecha valor: {Fecha(fechaValor)}");
        if (cfe.PeriodoDesde is not null || cfe.PeriodoHasta is not null)
            leyendas.Add($"Período facturado: {(cfe.PeriodoDesde is { } desde ? Fecha(desde) : "")} al {(cfe.PeriodoHasta is { } hasta ? Fecha(hasta) : "")}".Trim());
        return leyendas;
    }

    /// <summary>
    /// Leyenda de los CAE especiales, que va en un recuadro antes de la identificación del receptor:
    /// "Contribuyente Exonerado" (con la norma), "Contribuyente IVA MINIMO", "MONOTRIBUTO" o
    /// "MONOTRIBUTO SOCIAL MIDES".
    /// </summary>
    internal static string? LeyendaEmisor(CfeEmitido e) => e.Cae.Especial switch
    {
        CaeEspecial.Exonerado => "Contribuyente Exonerado"
            + (string.IsNullOrWhiteSpace(e.Emisor.NormaExoneracion) ? "" : $" - {e.Emisor.NormaExoneracion}"),
        CaeEspecial.IvaMinimo => "Contribuyente IVA MINIMO",
        CaeEspecial.Monotributo => "MONOTRIBUTO",
        CaeEspecial.MonotributoSocialMides => "MONOTRIBUTO SOCIAL MIDES",
        _ => null,
    };

    /// <summary>Identificación del receptor: un recuadro (con su valor, si lo tiene) y las líneas que lo siguen.</summary>
    internal sealed record IdentificacionReceptor(string? Recuadro, string? ValorRecuadro, IReadOnlyList<string> Lineas);

    /// <summary>
    /// Identificación del receptor según el tipo: "CONSUMO FINAL" en los e-Tickets (con el documento del
    /// receptor si se informó), "RUC COMPRADOR" con el RUT en las operaciones entre contribuyentes (e-Factura,
    /// e-Remito, e-Resguardo), "RUC VENDEDOR" en la e-Boleta de entrada y el nombre y domicilio en exportación.
    /// Con secreto profesional el receptor puede omitirse (su identificación va en la adenda).
    /// </summary>
    internal static IdentificacionReceptor Receptor(Cfe cfe)
    {
        var r = cfe.Receptor;
        var lineas = new List<string>();
        if (!string.IsNullOrWhiteSpace(r?.RazonSocial))
            lineas.Add(r.RazonSocial);
        var domicilio = string.Join(", ", new[] { r?.Direccion, r?.Ciudad, r?.Departamento, r?.Pais }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var familia = cfe.Tipo.Familia();
        var conRut = r is { TipoDocumento: TipoDocumentoReceptor.Rut } && !string.IsNullOrWhiteSpace(r.Documento)
            && (string.IsNullOrWhiteSpace(r.CodigoPais) || r.CodigoPais.Equals("UY", StringComparison.OrdinalIgnoreCase));

        if (familia == FamiliaCfe.Ticket)
        {
            if (!string.IsNullOrWhiteSpace(r?.Documento))
                lineas.Add(Documento(r));
            if (domicilio.Length > 0)
                lineas.Add(domicilio);
            return new IdentificacionReceptor("CONSUMO FINAL", null, lineas);
        }

        if (domicilio.Length > 0)
            lineas.Add(domicilio);
        if (familia is FamiliaCfe.Factura or FamiliaCfe.Remito or FamiliaCfe.Resguardo && conRut)
            return new IdentificacionReceptor("RUC COMPRADOR", r!.Documento, lineas);
        if (familia == FamiliaCfe.Boleta && conRut)
            return new IdentificacionReceptor("RUC VENDEDOR", r!.Documento, lineas);
        if (!string.IsNullOrWhiteSpace(r?.Documento))
            lineas.Insert(Math.Min(1, lineas.Count), Documento(r));
        return new IdentificacionReceptor(null, null, lineas);
    }

    private static string Documento(Receptor r) => DocumentoTexto(r.TipoDocumento, r.CodigoPais, r.Documento);

    /// <summary>Tipo, número y país de un documento (por ejemplo, <c>C.I. 12345672 (UY)</c>).</summary>
    private static string DocumentoTexto(TipoDocumentoReceptor tipoDocumento, string? codigoPais, string? documento)
    {
        var tipo = tipoDocumento switch
        {
            TipoDocumentoReceptor.Nie => "NIE",
            TipoDocumentoReceptor.Rut => "RUT",
            TipoDocumentoReceptor.CedulaIdentidad => "C.I.",
            TipoDocumentoReceptor.Pasaporte => "Pasaporte",
            TipoDocumentoReceptor.Dni => "DNI",
            TipoDocumentoReceptor.Nife => "NIFE",
            _ => "Documento",
        };
        var pais = string.IsNullOrWhiteSpace(codigoPais) ? "UY" : codigoPais.ToUpperInvariant();
        return $"{tipo} {documento} ({pais})";
    }

    /// <summary>Una línea de totales: etiqueta, monto y si se destaca (el total).</summary>
    internal sealed record LineaTotal(string Etiqueta, decimal Monto, bool Destacada = false);

    /// <summary>
    /// Totales que se imprimen: ajustes globales, montos netos e IVA por tasa (no para los contribuyentes IVA
    /// mínimo o Monotributo), el total, el monto no facturable y el total a pagar si difiere del total. El
    /// e-Remito no tiene montos y el e-Resguardo informa el total retenido.
    /// </summary>
    internal static IReadOnlyList<LineaTotal> LineasTotales(CfeEmitido e)
    {
        var (cfe, t) = (e.Documento, e.Totales);
        var familia = cfe.Tipo.Familia();
        var moneda = cfe.Moneda == Moneda.PesoUruguayo ? "" : $" ({cfe.Moneda.Codigo})";
        var lineas = new List<LineaTotal>();

        if (familia == FamiliaCfe.Remito)
            return lineas;
        if (familia == FamiliaCfe.Resguardo)
        {
            lineas.Add(new LineaTotal($"TOTAL RETENIDO{moneda}", t.MontoRetenido, Destacada: true));
            if (t.MontoCreditoFiscal != 0)
                lineas.Add(new LineaTotal($"Créditos fiscales{moneda}", t.MontoCreditoFiscal));
            return lineas;
        }

        foreach (var d in cfe.DescuentosRecargos)
            lineas.Add(new LineaTotal($"{(d.Tipo == TipoMovimientoGlobal.Descuento ? "Descuento" : "Recargo")} {d.Glosa}", cfe.MontoInformado(d)));

        if (familia == FamiliaCfe.Boleta)
        {
            if (t.MontoNoGravado > 0)
                lineas.Add(new LineaTotal("No gravado", t.MontoNoGravado));
            if (t.MontoNetoSuspendido > 0)
                lineas.Add(new LineaTotal("IVA en suspenso", t.MontoNetoSuspendido));
        }
        else if (!cfe.Tipo.EsExportacion() && !SinIva(e))
        {
            if (t.MontoNoGravado > 0)
                lineas.Add(new LineaTotal("Exento", t.MontoNoGravado));
            if (t.MontoExportacion > 0)
                lineas.Add(new LineaTotal("Exportación y asimiladas", t.MontoExportacion));
            if (t.MontoImpuestoPercibido > 0)
                lineas.Add(new LineaTotal("Impuesto percibido", t.MontoImpuestoPercibido));
            if (t.MontoNetoSuspendido > 0)
                lineas.Add(new LineaTotal("IVA en suspenso", t.MontoNetoSuspendido));
            if (t.MontoNetoMinimo > 0)
            {
                lineas.Add(new LineaTotal($"Neto IVA {Porcentaje(Totales.TasaIvaMinima)}", t.MontoNetoMinimo));
                lineas.Add(new LineaTotal($"IVA {Porcentaje(Totales.TasaIvaMinima)}", t.IvaMinimo));
            }
            if (t.MontoNetoBasico > 0)
            {
                lineas.Add(new LineaTotal($"Neto IVA {Porcentaje(Totales.TasaIvaBasica)}", t.MontoNetoBasico));
                lineas.Add(new LineaTotal($"IVA {Porcentaje(Totales.TasaIvaBasica)}", t.IvaBasico));
            }
            if (t.MontoNetoOtraTasa > 0)
            {
                lineas.Add(new LineaTotal("Neto IVA otra tasa/fictos", t.MontoNetoOtraTasa));
                lineas.Add(new LineaTotal("IVA otra tasa/fictos", t.IvaOtraTasa));
            }
        }

        // Retenciones, percepciones y créditos fiscales por código (A-C127 y A-C128), y sus totales.
        foreach (var retencion in cfe.RetencionesPorCodigo())
            lineas.Add(new LineaTotal($"{(retencion.EsCreditoFiscal ? "Crédito fiscal" : "Retención/percepción")} {retencion.Codigo}", retencion.Valor));
        if (t.MontoRetenido != 0)
            lineas.Add(new LineaTotal($"Total retenido/percibido{moneda}", t.MontoRetenido));
        if (t.MontoCreditoFiscal != 0)
            lineas.Add(new LineaTotal($"Créditos fiscales{moneda}", t.MontoCreditoFiscal));

        // Si el total a pagar es igual al total, alcanza con imprimir uno.
        var conTotalAPagar = familia != FamiliaCfe.RemitoExportacion && t.MontoPagar != t.MontoTotal;
        lineas.Add(new LineaTotal($"TOTAL{moneda}", t.MontoTotal, Destacada: !conTotalAPagar));
        if (conTotalAPagar)
        {
            if (t.MontoNoFacturable != 0)
                lineas.Add(new LineaTotal("No facturable", t.MontoNoFacturable));
            lineas.Add(new LineaTotal($"TOTAL A PAGAR{moneda}", t.MontoPagar, Destacada: true));
        }
        return lineas;
    }

    /// <summary>Moneda y tipo de cambio, si no es el peso uruguayo.</summary>
    internal static string? LineaMoneda(Cfe cfe) =>
        cfe.Moneda == Moneda.PesoUruguayo || cfe.Tipo.Familia() == FamiliaCfe.Remito
            ? null
            : $"Moneda: {cfe.Moneda.Codigo}" + (cfe.TipoCambio is { } tc ? $" - Tipo de cambio: {tc.ToString("N3", Cultura)}" : "");

    /// <summary>Los CFE a los que refiere (en las notas, el comprobante que corrigen).</summary>
    internal static IEnumerable<string> LineasReferencias(Cfe cfe) => cfe.Referencias.Select(r =>
        r.Global
            ? $"Referencia global: {r.Razon}"
            : $"{(r.TipoCfe is { } tipo ? NombreCompleto(tipo) : "CFE")} {r.Serie}-{r.NroCfe}"
              + (r.FechaCfe is { } fecha ? $" del {Fecha(fecha)}" : "")
              + (r.MontoCfeRef is { } monto ? $" por {(r.MonedaCfeRef is { } m ? m.Codigo + " " : "")}{Monto(monto)}" : "")
              + (string.IsNullOrWhiteSpace(r.Razon) ? "" : $" - {r.Razon}"));

    /// <summary>Subtotales informativos, en su orden de impresión.</summary>
    internal static IEnumerable<string> LineasSubtotales(Cfe cfe) =>
        cfe.SubtotalesInformativos.OrderBy(s => s.Orden ?? 0).Select(s => $"{s.Glosa}: {Monto(s.Valor)}");

    /// <summary>Quién es el mandante: en las ventas por cuenta ajena, por cuenta de quién se vende; en la e-Boleta, por cuenta de quién se compra.</summary>
    internal static string? LineaMandante(Cfe cfe) => cfe.Mandante is { } m
        ? $"{(cfe.Tipo.Familia() == FamiliaCfe.Boleta ? "Compra por cuenta de" : "Venta por cuenta ajena - Mandante")}: {m.Nombre} ({DocumentoTexto(m.TipoDocumento, m.CodigoPais, m.Documento)})"
        : null;

    /// <summary>Primeros 6 caracteres del código de seguridad, para imprimir bajo el QR.</summary>
    private static string CodigoSeguridadCorto(CfeEmitido e) => e.CodigoSeguridad[..Math.Min(6, e.CodigoSeguridad.Length)];

    /// <summary>
    /// Constancia de IVA al día, cuando corresponde (e-Tickets, e-Facturas y exportación; no los contribuyentes
    /// Monotributo): "IVA al día", o la falta de constancia del emisor o del mandante que informa el CFE.
    /// </summary>
    internal static string? ConstanciaIvaAlDia(CfeEmitido e)
    {
        if (e.Tipo.Familia() is not (FamiliaCfe.Ticket or FamiliaCfe.Factura or FamiliaCfe.FacturaExportacion)
            || e.Cae.Especial is CaeEspecial.Monotributo or CaeEspecial.MonotributoSocialMides)
            return null;
        return (e.Emisor.IvaAlDia, e.Documento.Mandante?.IvaAlDia ?? true) switch
        {
            (true, true) => "IVA al día",
            (false, true) => "Emisor sin constancia de IVA al día",
            (true, false) => "Mandante sin constancia de IVA al día",
            _ => "Emisor y mandante sin constancia de IVA al día",
        };
    }

    /// <summary>Líneas del pie junto al sello digital: leyenda del emisor, verificación, constancia de IVA al día, CAE y rango.</summary>
    internal static List<string> LineasSello(CfeEmitido e)
    {
        var lineas = new List<string>();
        if (!string.IsNullOrWhiteSpace(e.Emisor.FechaIncorporacion))
            lineas.Add($"Fecha emisor: {e.Emisor.FechaIncorporacion}");

        lineas.Add(e.Tipo.Familia() == FamiliaCfe.Ticket && !string.IsNullOrWhiteSpace(e.Emisor.UrlVerificacion)
            ? $"Puede verificar comprobante en {e.Emisor.UrlVerificacion}"
            : "Puede verificar comprobante en www.dgi.gub.uy");

        if (ConstanciaIvaAlDia(e) is { } constancia)
            lineas.Add(constancia);
        lineas.Add($"CAE Nº {e.Cae.NumeroAutorizacion}");
        lineas.Add($"Serie {e.Serie}, del {e.Cae.RangoDesde} al {e.Cae.RangoHasta}");
        return lineas;
    }

    // -----------------------------------------------------------------------
    // Formatos
    // -----------------------------------------------------------------------

    /// <summary>Un elemento de ancho fijo centrado en la fila.</summary>
    private static ContainerBuilder Centrado(RowBuilder fila, float ancho)
    {
        fila.RelativeItem();
        var item = fila.FixedItem(ancho);
        fila.RelativeItem();
        return item;
    }

    /// <summary>Recuadro (borde y relleno) con líneas de texto; los datos obligatorios en recuadro van con caracteres de 3 mm o más.</summary>
    private static void Recuadro(ContainerBuilder contenedor, float tamano, params string?[] lineas) =>
        contenedor.Border(1, "#000000").Padding(5).Column(c =>
        {
            foreach (var linea in lineas.OfType<string>())
                c.Item().AlignCenter().Text(linea).Bold().FontSize(tamano);
        });

    private static byte[] GenerarDocumentoA4(CfeEmitido e, byte[] qrBytes)
    {
        var (cfe, emisor) = (e.Documento, e.Emisor);
        var columnas = Columnas(e);
        var receptor = Receptor(cfe);
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginAll(42f); // ~1.5 cm en puntos

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        // Identificación del emisor (ángulo superior izquierdo): nombre, nombre comercial y domicilio fiscal.
                        row.RelativeItem(3).Column(bloque =>
                        {
                            bloque.Item().Text(emisor.RazonSocial).Bold().FontSize(14);
                            if (!string.IsNullOrWhiteSpace(emisor.NombreComercial))
                                bloque.Item().Text(emisor.NombreComercial).FontSize(10);
                            bloque.Item().Text(emisor.DomicilioFiscal).FontSize(9);
                            bloque.Item().Text($"{emisor.Ciudad}, {emisor.Departamento}").FontSize(9);
                            if (emisor.Telefonos.Count > 0)
                                bloque.Item().Text($"Tel.: {string.Join(" / ", emisor.Telefonos)}").FontSize(8);
                        });

                        // Identificación del comprobante (ángulo superior derecho): RUC, tipo, serie y número, forma de pago.
                        row.RelativeItem(2).AlignRight().Column(tipoDoc =>
                        {
                            tipoDoc.Item().Border(1, "#000000").Padding(8).AlignCenter().Column(c =>
                            {
                                c.Item().AlignCenter().Text($"RUC {emisor.Rut}").Bold().FontSize(11);
                                c.Item().AlignCenter().Text(NombreCompleto(cfe)).Bold().FontSize(12);
                                c.Item().AlignCenter().Text($"Serie {e.Serie} Nº {e.Numero}").Bold().FontSize(11);
                                if (FormaDePago(cfe) is { } formaPago)
                                    c.Item().AlignCenter().Text(formaPago).Bold().FontSize(11);
                                c.Item().AlignCenter().Text($"Fecha: {Fecha(e.FechaEmision)}").FontSize(9);
                            });
                        });
                    });

                    col.Item().Line(1, "#9E9E9E");
                });

                page.Content().Column(col =>
                {
                    // Leyenda del CAE especial: recuadro de al menos 4 x 1 cm antes del receptor.
                    if (LeyendaEmisor(e) is { } leyenda)
                        col.Item().PaddingTop(5).Row(row => Recuadro(row.FixedItem(Math.Max(130, leyenda.Length * 7f)), 11, leyenda));

                    // Leyendas obligatorias (bienes de terceros, pagos por cuenta de terceros, fecha valor), de 3 mm o más.
                    foreach (var leyendaComprobante in Leyendas(cfe))
                        col.Item().PaddingTop(3).Text(leyendaComprobante).Bold().FontSize(9);

                    // Identificación del receptor: recuadro "CONSUMO FINAL" o "RUC COMPRADOR" (4 x 1 cm) y sus datos.
                    col.Item().PaddingVertical(5).Row(row =>
                    {
                        if (receptor.Recuadro is { } recuadro)
                            Recuadro(row.FixedItem(130), 11, recuadro, receptor.ValorRecuadro);
                        row.RelativeItem().PaddingLeft(receptor.Recuadro is null ? 0 : 10).Column(rec =>
                        {
                            foreach (var linea in receptor.Lineas)
                                rec.Item().Text(linea).FontSize(9);
                        });
                    });

                    if (LineaMoneda(cfe) is { } moneda)
                        col.Item().PaddingTop(5).Text(moneda).FontSize(9);

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            foreach (var c in columnas)
                            {
                                if (c.AnchoFijo)
                                    cols.ConstantColumn(c.Ancho);
                                else
                                    cols.RelativeColumn(c.Ancho);
                            }
                        });

                        table.Header(h =>
                        {
                            foreach (var c in columnas)
                                h.Cell().Background("#EEEEEE").Padding(4).AlignCenter().Text(c.Titulo).Bold().FontSize(9);
                        });

                        foreach (var fila in Filas(e))
                        {
                            for (var i = 0; i < columnas.Length; i++)
                            {
                                var celda = table.Cell().Padding(4);
                                (columnas[i].Derecha ? celda.AlignRight() : celda).Text(fila[i]).FontSize(9);
                            }
                        }
                    });

                    col.Item().PaddingTop(10).AlignRight().Column(tot =>
                    {
                        foreach (var linea in LineasTotales(e))
                        {
                            var texto = tot.Item().Text($"{linea.Etiqueta}: {Monto(linea.Monto)}");
                            if (linea.Destacada)
                                texto.Bold().FontSize(12);
                            else
                                texto.FontSize(9);
                        }
                    });

                    if (cfe.MediosPago.Count > 0)
                    {
                        col.Item().PaddingTop(8).Column(mp =>
                        {
                            mp.Item().Text("Medios de pago:").Bold().FontSize(9);
                            foreach (var m in cfe.MediosPago.OrderBy(m => m.Orden ?? 0))
                                mp.Item().Text($"{m.Glosa}: {Monto(m.Valor)}").FontSize(9);
                        });
                    }

                    if (cfe.SubtotalesInformativos.Count > 0)
                    {
                        col.Item().PaddingTop(8).Column(st =>
                        {
                            st.Item().Text("Subtotales:").Bold().FontSize(9);
                            foreach (var subtotal in LineasSubtotales(cfe))
                                st.Item().Text(subtotal).FontSize(9);
                        });
                    }

                    if (cfe.Referencias.Count > 0)
                    {
                        col.Item().PaddingTop(8).Column(rf =>
                        {
                            rf.Item().Text("Referencias:").Bold().FontSize(9);
                            foreach (var referencia in LineasReferencias(cfe))
                                rf.Item().Text(referencia).FontSize(9);
                        });
                    }

                    if (LineaMandante(cfe) is { } mandante)
                        col.Item().PaddingTop(8).Text(mandante).FontSize(9);

                    if (!string.IsNullOrWhiteSpace(cfe.InfoAdicional))
                        col.Item().PaddingTop(8).Text(cfe.InfoAdicional).FontSize(8);

                    // Pie: sello digital (QR de 22 a 30 mm y código de seguridad), leyendas y recuadro del vencimiento del CAE.
                    col.Item().PaddingTop(20).Row(row =>
                    {
                        row.FixedItem(80).Column(sello =>
                        {
                            sello.Item().Image(qrBytes);
                            sello.Item().AlignCenter().Text($"Código de seguridad: {CodigoSeguridadCorto(e)}").FontSize(6);
                        });

                        row.RelativeItem().PaddingLeft(10).Column(qrInfo =>
                        {
                            foreach (var linea in LineasSello(e))
                                qrInfo.Item().Text(linea).FontSize(8);
                            if (e.AceptadoPorDgi)
                                qrInfo.Item().Text("Aceptado por DGI").FontSize(8).Color("#388E3C");
                        });

                        Recuadro(row.FixedItem(110), 11, "Fecha de vencimiento", Fecha(e.Cae.FechaVencimiento));
                    });

                    // La adenda, después del CFE y en una zona delimitada con la mención "ADENDA".
                    if (!string.IsNullOrWhiteSpace(cfe.Adenda))
                    {
                        // El relleno de un elemento queda dentro de su borde: la separación va aparte.
                        col.Item().Spacer(15);
                        col.Item().Border(1, "#9E9E9E").Padding(8).Column(ad =>
                        {
                            ad.Item().Text("ADENDA").Bold().FontSize(10);
                            ad.Item().Text(cfe.Adenda).FontSize(9);
                        });
                    }
                });

                page.Footer().Row(row =>
                {
                    void EstiloFooter(TextStyle s) { s.FontSize = 7; s.Color = ReportColor.FromHex("#9E9E9E"); }
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.Span("Página ", EstiloFooter);
                        text.CurrentPageNumber(EstiloFooter);
                        text.Span(" de ", EstiloFooter);
                        text.TotalPages(EstiloFooter);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static byte[] GenerarDocumentoTermico(CfeEmitido e, byte[] qrBytes)
    {
        var (cfe, emisor) = (e.Documento, e.Emisor);
        var familia = cfe.Tipo.Familia();
        var receptor = Receptor(cfe);
        // Ancho térmico estándar: 80mm ≈ 227 puntos; alto generoso para contenido variable
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(227, 800);
                page.MarginAll(5);

                page.Content().Column(col =>
                {
                    col.Item().AlignCenter().Text(emisor.RazonSocial).Bold().FontSize(9);
                    if (!string.IsNullOrWhiteSpace(emisor.NombreComercial))
                        col.Item().AlignCenter().Text(emisor.NombreComercial).FontSize(7);
                    col.Item().AlignCenter().Text(emisor.DomicilioFiscal).FontSize(7);
                    col.Item().Line(0.5f, "#000000");

                    col.Item().AlignCenter().Text($"RUC {emisor.Rut}").Bold().FontSize(9);
                    col.Item().AlignCenter().Text(NombreCompleto(cfe)).Bold().FontSize(9);
                    col.Item().AlignCenter().Text($"Serie {e.Serie} Nº {e.Numero}").Bold().FontSize(9);
                    if (FormaDePago(cfe) is { } formaPago)
                        col.Item().AlignCenter().Text(formaPago).Bold().FontSize(9);
                    col.Item().AlignCenter().Text($"Fecha: {Fecha(e.FechaEmision)}").FontSize(7);
                    foreach (var leyendaComprobante in Leyendas(cfe))
                        col.Item().AlignCenter().Text(leyendaComprobante).Bold().FontSize(9);
                    col.Item().Line(0.5f, "#000000");

                    if (LeyendaEmisor(e) is { } leyenda)
                        col.Item().PaddingVertical(3).Row(row => Recuadro(Centrado(row, 170), 9, leyenda));

                    if (receptor.Recuadro is { } recuadro)
                        col.Item().PaddingVertical(3).Row(row => Recuadro(Centrado(row, 130), 9, recuadro, receptor.ValorRecuadro));
                    foreach (var linea in receptor.Lineas)
                        col.Item().Text(linea).FontSize(7);
                    col.Item().Line(0.5f, "#000000");

                    if (LineaMoneda(cfe) is { } moneda)
                        col.Item().Text(moneda).FontSize(7);

                    // Fila "etiqueta ........ valor" del ticket.
                    void Fila(string etiqueta, string valor, bool negrita = false) => col.Item().Row(r =>
                    {
                        var izquierda = r.RelativeItem().Text(etiqueta).FontSize(7);
                        var derecha = r.Item().AlignRight().Text(valor).FontSize(7);
                        if (negrita)
                        {
                            izquierda.Bold();
                            derecha.Bold();
                        }
                    });

                    switch (familia)
                    {
                        case FamiliaCfe.Remito:
                            foreach (var linea in cfe.Detalle)
                                Fila(linea.NombreItem, ConSigno(linea, Cantidad(linea.Cantidad)));
                            break;

                        case FamiliaCfe.Resguardo:
                            foreach (var linea in cfe.Detalle)
                                foreach (var r in linea.Retenciones)
                                    Fila($"{r.Codigo}{(r.Tasa is { } tasa ? $" {Porcentaje(tasa)}" : "")} sobre {Monto(r.MontoSujeto)}", ConSigno(linea, Monto(r.Valor)));
                            break;

                        default:
                            var conIva = familia is FamiliaCfe.Ticket or FamiliaCfe.Factura && !SinIva(e);
                            foreach (var linea in cfe.Detalle)
                            {
                                col.Item().Text(Descripcion(linea)).FontSize(7);
                                var indicador = conIva && EtiquetaIndicador(linea.IndFact) is { Length: > 0 } etiqueta ? $" ({etiqueta})" : "";
                                Fila($"  {Cantidad(linea.Cantidad)} x {Precio(linea.PrecioUnitario)}{indicador}", MontoLinea(cfe, linea));
                            }
                            break;
                    }

                    col.Item().Line(0.5f, "#000000");

                    foreach (var linea in LineasTotales(e))
                        Fila($"{linea.Etiqueta}:", Monto(linea.Monto), negrita: linea.Destacada);
                    foreach (var m in cfe.MediosPago.OrderBy(m => m.Orden ?? 0))
                        Fila($"{m.Glosa}:", Monto(m.Valor));
                    foreach (var subtotal in LineasSubtotales(cfe))
                        col.Item().Text(subtotal).FontSize(6);

                    if (cfe.Referencias.Count > 0)
                    {
                        col.Item().Line(0.5f, "#000000");
                        foreach (var referencia in LineasReferencias(cfe))
                            col.Item().Text($"Ref.: {referencia}").FontSize(6);
                    }

                    if (LineaMandante(cfe) is { } mandante)
                        col.Item().Text(mandante).FontSize(6);

                    col.Item().Line(0.5f, "#000000");

                    col.Item().PaddingTop(5).Row(row => Centrado(row, 70).Image(qrBytes));
                    col.Item().AlignCenter().Text($"Código de seguridad: {CodigoSeguridadCorto(e)}").FontSize(6);
                    foreach (var linea in LineasSello(e))
                        col.Item().AlignCenter().Text(linea).FontSize(6);
                    col.Item().PaddingTop(3).Row(row => Recuadro(Centrado(row, 100), 9, "Fecha de vencimiento", Fecha(e.Cae.FechaVencimiento)));

                    if (!string.IsNullOrWhiteSpace(cfe.Adenda))
                    {
                        col.Item().PaddingTop(5).Line(0.5f, "#000000");
                        col.Item().Text("ADENDA").Bold().FontSize(7);
                        col.Item().Text(cfe.Adenda).FontSize(6);
                    }
                });
            });
        }).GeneratePdf();
    }

    /// <summary>Nombre del comprobante para la representación impresa, como en la tabla A del formato de los CFE.</summary>
    internal static string NombreCompleto(TipoCfe tipo)
    {
        var baseNombre = tipo.Familia() switch
        {
            FamiliaCfe.Ticket => "e-Ticket",
            FamiliaCfe.Factura => "e-Factura",
            FamiliaCfe.FacturaExportacion => "e-Factura Exportación",
            FamiliaCfe.Remito => "e-Remito",
            FamiliaCfe.RemitoExportacion => "e-Remito de Exportación",
            FamiliaCfe.Boleta => "e-Boleta de entrada",
            _ => "e-Resguardo",
        };

        var nombre = tipo.EsNotaCorreccion()
            ? $"{(tipo.EsNotaCredito() ? "Nota de Crédito" : "Nota de Débito")} de {baseNombre}"
            : baseNombre;
        if (tipo.EsCuentaAjena()) nombre += " Venta por Cuenta Ajena";
        if (tipo.EsContingencia()) nombre += " Contingencia";
        return nombre;
    }

    /// <summary>Nombre del comprobante seguido, en una cobranza propia, de la leyenda "Cobranza".</summary>
    internal static string NombreCompleto(Cfe cfe) => cfe.CobranzaPropia ? $"{NombreCompleto(cfe.Tipo)} Cobranza" : NombreCompleto(cfe.Tipo);
}
