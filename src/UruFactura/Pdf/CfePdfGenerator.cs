using FluentReport;
using FluentReport.Core;
using FluentReport.Styling;
using System.Globalization;
using SkiaSharp;
using UruFactura.Configuration;
using UruFactura.Enums;
using UruFactura.Exceptions;
using UruFactura.Formatting;
using UruFactura.Models;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.Rendering;

namespace UruFactura.Pdf;

/// <summary>
/// Genera la representación impresa (PDF) de los CFE en formato A4 o térmico (ticket),
/// incluyendo el código QR y los datos requeridos por normativa DGI.
/// </summary>
public class CfePdfGenerator : ICfePdfGenerator
{
    private readonly UruFacturaConfig _config;
    private readonly ICfeQrGenerator _qrGenerator;

    /// <summary>
    /// Inicializa el generador usando el generador de QR predeterminado.
    /// </summary>
    /// <param name="config">Configuración de UruFactura.</param>
    public CfePdfGenerator(UruFacturaConfig config)
        : this(config, new CfeQrGenerator())
    {
    }

    /// <summary>
    /// Inicializa el generador con un generador de QR personalizado.
    /// </summary>
    /// <param name="config">Configuración de UruFactura.</param>
    /// <param name="qrGenerator">Implementación de <see cref="ICfeQrGenerator"/> a utilizar.</param>
    public CfePdfGenerator(UruFacturaConfig config, ICfeQrGenerator qrGenerator)
    {
        ArgumentNullException.ThrowIfNull(qrGenerator);
        _config = config;
        _qrGenerator = qrGenerator;
    }

    /// <inheritdoc />
    public virtual byte[] GenerarA4(Cfe cfe)
    {
        try
        {
            var qrBytes = _qrGenerator.GenerarQrCode(cfe, _config.Ambiente);
            return GenerarDocumentoA4(cfe, _config, qrBytes);
        }
        catch (Exception ex)
        {
            throw new PdfGenerationException("Error al generar el PDF A4 del CFE.", ex);
        }
    }

    /// <inheritdoc />
    public virtual byte[] GenerarTermico(Cfe cfe)
    {
        try
        {
            var qrBytes = _qrGenerator.GenerarQrCode(cfe, _config.Ambiente);
            return GenerarDocumentoTermico(cfe, _config, qrBytes);
        }
        catch (Exception ex)
        {
            throw new PdfGenerationException("Error al generar el PDF térmico del CFE.", ex);
        }
    }

    /// <summary>
    /// Genera el código QR del CFE según normativa DGI.
    /// El contenido del QR es la URL de consulta con los datos del comprobante.
    /// Usa el ambiente de <b>Producción</b> por defecto.
    /// </summary>
    /// <param name="cfe">El CFE.</param>
    public static byte[] GenerarQrCode(Cfe cfe) =>
        GenerarQrCode(cfe, Ambiente.Produccion);

    /// <summary>
    /// Genera el código QR del CFE según normativa DGI.
    /// El contenido del QR es la URL de consulta con los datos del comprobante.
    /// </summary>
    /// <param name="cfe">El CFE.</param>
    /// <param name="ambiente">Ambiente de operación (Homologación o Producción).</param>
    public static byte[] GenerarQrCode(Cfe cfe, Ambiente ambiente)
    {
        var contenidoQr = ConstruirContenidoQr(cfe, ambiente);

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

        var pixelData = writer.Write(contenidoQr);

        // Convert raw BGRA pixel data to PNG using SkiaSharp
        using var bitmap = new SKBitmap(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(
            pixelData.Pixels, 0, bitmap.GetPixels(), pixelData.Pixels.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>URL de consulta del QR según el formato CFE de la DGI (zona "Pie del comprobante").</summary>
    internal const string UrlConsultaQr = "https://www.efactura.dgi.gub.uy/consultaQR/cfe";

    /// <summary>
    /// Contenido del QR: <c>https://www.efactura.dgi.gub.uy/consultaQR/cfe?ruc,tipoCFE,serie,nroCFE,monto,fecha,hash</c>,
    /// con la fecha de firma en dd/MM/yyyy y el código de seguridad (hash de la firma). Si el CFE todavía
    /// no fue firmado la fecha es la de emisión y el hash queda vacío (solo para vista previa).
    /// </summary>
    internal static string ConstruirContenidoQr(Cfe cfe, Ambiente ambiente)
    {
        var fecha = cfe.FechaFirma is { } firma
            ? firma.ToOffset(HoraUruguay.Offset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : cfe.FechaEmision.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        return $"{UrlConsultaQr}?" + string.Join(",",
            cfe.RutEmisor,
            ((int)cfe.Tipo).ToString(CultureInfo.InvariantCulture),
            cfe.Serie ?? string.Empty,
            cfe.Numero.ToString(CultureInfo.InvariantCulture),
            CfeFormat.DecimalInvariant(MontoQr(cfe), "F2"),
            fecha,
            Uri.EscapeDataString(cfe.CodigoSeguridad ?? string.Empty));
    }

    /// <summary>Monto que se informa en el QR: e-Remito 0, e-Resguardo el total retenido, resto el total a pagar.</summary>
    private static decimal MontoQr(Cfe cfe) => cfe.Tipo.Familia() switch
    {
        FamiliaCfe.Remito => 0m,
        FamiliaCfe.Resguardo => cfe.MontoRetenido,
        _ => cfe.MontoTotal, // en exportación, igual al monto de exportación
    };

    /// <summary>Primeros 6 caracteres del código de seguridad, para imprimir bajo el QR.</summary>
    private static string CodigoSeguridadCorto(Cfe cfe) =>
        string.IsNullOrEmpty(cfe.CodigoSeguridad) ? "------" : cfe.CodigoSeguridad[..Math.Min(6, cfe.CodigoSeguridad.Length)];

    /// <summary>Líneas del pie del comprobante: leyenda del emisor, verificación, CAE, rango y vencimiento.</summary>
    private static List<string> LineasSello(Cfe cfe, UruFacturaConfig config)
    {
        var lineas = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.FechaIncorporacionEmisor))
            lineas.Add($"Fecha emisor: {config.FechaIncorporacionEmisor}");

        var esTicket = cfe.Tipo.Familia() == FamiliaCfe.Ticket;
        lineas.Add(esTicket && !string.IsNullOrWhiteSpace(config.UrlVerificacionEmisor)
            ? $"Puede verificar comprobante en {config.UrlVerificacionEmisor}"
            : "Puede verificar comprobante en www.dgi.gub.uy");

        if (cfe.CaeData is { } cae)
        {
            lineas.Add($"CAE Nº {cae.NumeroAutorizacion}");
            lineas.Add($"Serie {cfe.Serie}, del {cae.RangoDesde} al {cae.RangoHasta}");
            lineas.Add($"Fecha de vencimiento: {cae.FechaVencimiento:dd/MM/yyyy}");
        }
        return lineas;
    }

    private static byte[] GenerarDocumentoA4(Cfe cfe, UruFacturaConfig config, byte[] qrBytes)
    {
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
                        row.RelativeItem(3).Column(emisor =>
                        {
                            emisor.Item().Text(config.RazonSocialEmisor).Bold().FontSize(14);
                            if (!string.IsNullOrWhiteSpace(config.NombreComercialEmisor))
                                emisor.Item().Text(config.NombreComercialEmisor).FontSize(10);
                            emisor.Item().Text($"RUT: {config.RutEmisor}").FontSize(9);
                            emisor.Item().Text(config.DomicilioFiscal).FontSize(9);
                            emisor.Item().Text($"{config.Ciudad}, {config.Departamento}").FontSize(9);
                        });

                        row.RelativeItem(2).AlignRight().Column(tipoDoc =>
                        {
                            tipoDoc.Item().Border(1).Padding(8).AlignCenter().Column(c =>
                            {
                                c.Item().Text(NombreCompleto(cfe.Tipo)).Bold().FontSize(12);
                                c.Item().Text($"Serie {cfe.Serie} N° {cfe.Numero}").Bold().FontSize(11);
                                c.Item().Text($"Fecha: {cfe.FechaEmision:dd/MM/yyyy}").FontSize(9);
                            });
                        });
                    });

                    col.Item().Line(1, "#9E9E9E");
                });

                page.Content().Column(col =>
                {
                    if (cfe.Receptor != null)
                    {
                        col.Item().PaddingVertical(5).Column(rec =>
                        {
                            rec.Item().Text("Receptor:").Bold().FontSize(9);
                            if (!string.IsNullOrWhiteSpace(cfe.Receptor.RazonSocial))
                                rec.Item().Text(cfe.Receptor.RazonSocial).FontSize(9);
                            if (!string.IsNullOrWhiteSpace(cfe.Receptor.Documento))
                                rec.Item().Text($"RUT/Doc: {cfe.Receptor.Documento}").FontSize(9);
                            if (!string.IsNullOrWhiteSpace(cfe.Receptor.Direccion))
                                rec.Item().Text(cfe.Receptor.Direccion).FontSize(9);
                        });

                        col.Item().Line(0.5f, "#F5F5F5");
                    }

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(40);  // Cant
                            cols.RelativeColumn(5);   // Descripción
                            cols.RelativeColumn(2);   // P.Unit
                            cols.RelativeColumn(1);   // IVA
                            cols.RelativeColumn(2);   // Total
                        });

                        table.Header(h =>
                        {
                            foreach (var titulo in (string[])["Cant.", "Descripción", "P.Unit.", "IVA", "Total"])
                                h.Cell().Background("#EEEEEE").Padding(4).AlignCenter().Text(titulo).Bold().FontSize(9);
                        });

                        foreach (var linea in cfe.Detalle)
                        {
                            table.Cell().Padding(4).AlignRight().Text(CfeFormat.MonetaryPdf(linea.Cantidad, "F2")).FontSize(9);
                            table.Cell().Padding(4).Text(linea.NombreItem).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(CfeFormat.MonetaryPdf(linea.PrecioUnitario)).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(ObtenerEtiquetaIva(linea.IndFactIva)).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(CfeFormat.MonetaryPdf(linea.MontoTotal)).FontSize(9);
                        }
                    });

                    col.Item().PaddingTop(10).AlignRight().Column(tot =>
                    {
                        if (cfe.MontoNetoExento > 0)
                            tot.Item().Text($"Exento: {CfeFormat.MonetaryPdf(cfe.MontoNetoExento)}").FontSize(9);
                        if (cfe.MontoNetoMinimo > 0)
                        {
                            tot.Item().Text($"Neto IVA 10%: {CfeFormat.MonetaryPdf(cfe.MontoNetoMinimo)}").FontSize(9);
                            tot.Item().Text($"IVA 10%: {CfeFormat.MonetaryPdf(cfe.IvaMinimo)}").FontSize(9);
                        }
                        if (cfe.MontoNetoBasico > 0)
                        {
                            tot.Item().Text($"Neto IVA 22%: {CfeFormat.MonetaryPdf(cfe.MontoNetoBasico)}").FontSize(9);
                            tot.Item().Text($"IVA 22%: {CfeFormat.MonetaryPdf(cfe.IvaBasico)}").FontSize(9);
                        }
                        foreach (var d in cfe.DescuentosRecargos)
                            tot.Item().Text($"{(d.Tipo == TipoMovimientoGlobal.Descuento ? "Descuento" : "Recargo")} {d.Glosa}: {CfeFormat.MonetaryPdf(d.Monto)}").FontSize(9);
                        if (cfe.Tipo.Familia() == FamiliaCfe.Resguardo)
                            tot.Item().Text($"TOTAL RETENIDO: {CfeFormat.MonetaryPdf(cfe.MontoRetenido)}").Bold().FontSize(12);
                        else
                            tot.Item().Text($"TOTAL{(cfe.Moneda == Moneda.PesoUruguayo ? "" : $" ({cfe.Moneda})")}: {CfeFormat.MonetaryPdf(cfe.MontoTotal)}").Bold().FontSize(12);
                    });

                    if (cfe.MediosPago.Count > 0)
                    {
                        col.Item().PaddingTop(8).Column(mp =>
                        {
                            mp.Item().Text("Medios de pago:").Bold().FontSize(9);
                            foreach (var m in cfe.MediosPago.OrderBy(m => m.Orden ?? 0))
                                mp.Item().Text($"{m.Glosa}: {CfeFormat.MonetaryPdf(m.Valor)}").FontSize(9);
                        });
                    }

                    if (cfe.Mandante is { } mandante)
                    {
                        col.Item().PaddingTop(8).Column(md =>
                        {
                            md.Item().Text("Venta por cuenta ajena - Mandante:").Bold().FontSize(9);
                            md.Item().Text($"{mandante.Nombre} ({mandante.Documento})").FontSize(9);
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(cfe.InfoAdicional))
                        col.Item().PaddingTop(8).Text(cfe.InfoAdicional).FontSize(8);

                    col.Item().PaddingTop(20).Row(row =>
                    {
                        row.FixedItem(80).Column(sello =>
                        {
                            sello.Item().Image(qrBytes);
                            sello.Item().AlignCenter().Text($"Código de seguridad: {CodigoSeguridadCorto(cfe)}").FontSize(6);
                        });

                        row.RelativeItem().PaddingLeft(10).Column(qrInfo =>
                        {
                            foreach (var linea in LineasSello(cfe, config))
                                qrInfo.Item().Text(linea).FontSize(8);
                            qrInfo.Item().Text($"Ambiente: {config.Ambiente}").FontSize(8);
                            if (cfe.AceptadoPorDgi)
                                qrInfo.Item().Text("Aceptado por DGI").FontSize(8).Color("#388E3C");
                        });
                    });
                });

                page.Footer().Row(row =>
                {
                    void EstiloFooter(TextStyle s) { s.FontSize = 7; s.Color = ReportColor.FromHex("#9E9E9E"); }
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("Generado con UruFactura - ", EstiloFooter);
                        text.Span("Página ", EstiloFooter);
                        text.CurrentPageNumber(EstiloFooter);
                        text.Span(" de ", EstiloFooter);
                        text.TotalPages(EstiloFooter);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static byte[] GenerarDocumentoTermico(Cfe cfe, UruFacturaConfig config, byte[] qrBytes)
    {
        // Ancho térmico estándar: 80mm ≈ 227 puntos; alto generoso para contenido variable
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(227, 800);
                page.MarginAll(5);

                page.Content().Column(col =>
                {
                    col.Item().AlignCenter().Text(config.RazonSocialEmisor).Bold().FontSize(9);
                    col.Item().AlignCenter().Text($"RUT: {config.RutEmisor}").FontSize(7);
                    col.Item().AlignCenter().Text(config.DomicilioFiscal).FontSize(7);
                    col.Item().Line(0.5f);

                    col.Item().AlignCenter().Text(NombreCompleto(cfe.Tipo)).Bold().FontSize(8);
                    col.Item().AlignCenter().Text($"Serie {cfe.Serie} N° {cfe.Numero}").Bold().FontSize(7);
                    col.Item().AlignCenter().Text($"Fecha: {cfe.FechaEmision:dd/MM/yyyy}").FontSize(7);
                    col.Item().Line(0.5f);

                    if (cfe.Receptor?.RazonSocial != null)
                    {
                        col.Item().Text($"Cliente: {cfe.Receptor.RazonSocial}").FontSize(7);
                        col.Item().Line(0.5f);
                    }

                    foreach (var linea in cfe.Detalle)
                    {
                        col.Item().Text(linea.NombreItem).FontSize(7);
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"  {CfeFormat.MonetaryPdf(linea.Cantidad, "F2")} x {CfeFormat.MonetaryPdf(linea.PrecioUnitario)}").FontSize(7);
                            row.Item().AlignRight().Text(CfeFormat.MonetaryPdf(linea.MontoTotal)).FontSize(7);
                        });
                    }

                    col.Item().Line(0.5f);

                    // Fila "etiqueta ........ monto" del ticket.
                    void Fila(string etiqueta, decimal monto, bool negrita = false) => col.Item().Row(r =>
                    {
                        var izquierda = r.RelativeItem().Text(etiqueta).FontSize(7);
                        var derecha = r.Item().AlignRight().Text(CfeFormat.MonetaryPdf(monto)).FontSize(7);
                        if (negrita)
                        {
                            izquierda.Bold();
                            derecha.Bold();
                        }
                    });

                    if (cfe.MontoNetoExento > 0)
                        Fila("Exento:", cfe.MontoNetoExento);
                    if (cfe.IvaMinimo > 0)
                        Fila("IVA 10%:", cfe.IvaMinimo);
                    if (cfe.IvaBasico > 0)
                        Fila("IVA 22%:", cfe.IvaBasico);
                    foreach (var d in cfe.DescuentosRecargos)
                        Fila(d.Tipo == TipoMovimientoGlobal.Descuento ? $"Desc. {d.Glosa}:" : $"Rec. {d.Glosa}:", d.Monto);
                    Fila("TOTAL:", cfe.MontoTotal, negrita: true);
                    foreach (var m in cfe.MediosPago.OrderBy(m => m.Orden ?? 0))
                        Fila($"{m.Glosa}:", m.Valor);

                    col.Item().Line(0.5f);

                    col.Item().PaddingTop(5).AlignCenter().Row(row =>
                    {
                        row.FixedItem(70).Image(qrBytes);
                    });
                    col.Item().AlignCenter().Text($"Código de seguridad: {CodigoSeguridadCorto(cfe)}").FontSize(6);
                    foreach (var linea in LineasSello(cfe, config))
                        col.Item().AlignCenter().Text(linea).FontSize(6);
                });
            });
        }).GeneratePdf();
    }

    /// <summary>Nombre del comprobante para la representación impresa.</summary>
    internal static string NombreCompleto(TipoCfe tipo)
    {
        var baseNombre = tipo.Familia() switch
        {
            FamiliaCfe.Ticket => "e-Ticket",
            FamiliaCfe.Factura => "e-Factura",
            FamiliaCfe.FacturaExportacion => "e-Factura de Exportación",
            FamiliaCfe.Remito => "e-Remito",
            FamiliaCfe.RemitoExportacion => "e-Remito de Exportación",
            _ => "e-Resguardo",
        };

        var nombre = tipo.EsNotaCorreccion()
            ? $"{(tipo.EsNotaCredito() ? "Nota de Crédito" : "Nota de Débito")} {baseNombre}"
            : baseNombre;
        if (tipo.EsCuentaAjena()) nombre += " Cuenta Ajena";
        if (tipo.EsContingencia()) nombre += " (Contingencia)";
        return nombre;
    }

    private static string ObtenerEtiquetaIva(TipoIva iva) => iva switch
    {
        TipoIva.Exento => "EXE",
        TipoIva.Minimo => "10%",
        TipoIva.Basico => "22%",
        TipoIva.Suspendido => "SUS",
        _ => "N/A",
    };
}
