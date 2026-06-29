using FluentReport;
using FluentReport.Core;
using FluentReport.Styling;
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
    public byte[] GenerarA4(Cfe cfe)
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
    public byte[] GenerarTermico(Cfe cfe)
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

    private static string ConstruirContenidoQr(Cfe cfe, Ambiente ambiente)
    {
        // Formato de URL de verificación DGI Uruguay
        var baseUrl = ambiente == Ambiente.Produccion
            ? "https://efactura.dgi.gub.uy/efactura/valida"
            : "https://efacturahomologacion.dgi.gub.uy/efactura/valida";

        return $"{baseUrl}?" +
               $"ruc={Uri.EscapeDataString(cfe.RutEmisor)}" +
               $"&tipoCfe={(int)cfe.Tipo}" +
               $"&serie={Uri.EscapeDataString(cfe.Serie ?? string.Empty)}" +
               $"&nro={cfe.Numero}" +
               $"&fecha={CfeFormat.DateCompact(cfe.FechaEmision)}" +
               $"&monto={CfeFormat.DecimalInvariant(cfe.MontoTotal, "F2")}";
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
                                c.Item().Text(ObtenerNombreTipo(cfe)).Bold().FontSize(12);
                                c.Item().Text($"N° {cfe.Serie}{cfe.Numero:D8}").Bold().FontSize(11);
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
                            h.Cell().Background("#EEEEEE").Padding(4).AlignCenter().Text("Cant.").Bold().FontSize(9);
                            h.Cell().Background("#EEEEEE").Padding(4).AlignCenter().Text("Descripción").Bold().FontSize(9);
                            h.Cell().Background("#EEEEEE").Padding(4).AlignCenter().Text("P.Unit.").Bold().FontSize(9);
                            h.Cell().Background("#EEEEEE").Padding(4).AlignCenter().Text("IVA").Bold().FontSize(9);
                            h.Cell().Background("#EEEEEE").Padding(4).AlignCenter().Text("Total").Bold().FontSize(9);
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
                        tot.Item().Text($"TOTAL: {CfeFormat.MonetaryPdf(cfe.MontoTotal)}").Bold().FontSize(12);
                    });

                    col.Item().PaddingTop(20).Row(row =>
                    {
                        row.FixedItem(80).Image(qrBytes);

                        row.RelativeItem().PaddingLeft(10).Column(qrInfo =>
                        {
                            qrInfo.Item().Text("Representación impresa de CFE").Italic().FontSize(8);
                            qrInfo.Item().Text("Verifique la vigencia en: efactura.dgi.gub.uy").FontSize(8);
                            qrInfo.Item().Text($"Ambiente: {config.Ambiente}").FontSize(8);
                            if (cfe.AceptadoPorDgi)
                                qrInfo.Item().Text("✅ Aceptado por DGI").FontSize(8).Color("#388E3C");
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

                    col.Item().AlignCenter().Text(ObtenerNombreTipoTermico(cfe)).Bold().FontSize(8);
                    col.Item().AlignCenter().Text($"N° {cfe.Serie}{cfe.Numero:D8}").Bold().FontSize(7);
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

                    if (cfe.MontoNetoExento > 0)
                        col.Item().Row(r => { r.RelativeItem().Text("Exento:").FontSize(7); r.Item().AlignRight().Text(CfeFormat.MonetaryPdf(cfe.MontoNetoExento)).FontSize(7); });
                    if (cfe.IvaMinimo > 0)
                        col.Item().Row(r => { r.RelativeItem().Text("IVA 10%:").FontSize(7); r.Item().AlignRight().Text(CfeFormat.MonetaryPdf(cfe.IvaMinimo)).FontSize(7); });
                    if (cfe.IvaBasico > 0)
                        col.Item().Row(r => { r.RelativeItem().Text("IVA 22%:").FontSize(7); r.Item().AlignRight().Text(CfeFormat.MonetaryPdf(cfe.IvaBasico)).FontSize(7); });

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("TOTAL:").Bold().FontSize(7);
                        r.Item().AlignRight().Text(CfeFormat.MonetaryPdf(cfe.MontoTotal)).Bold().FontSize(7);
                    });

                    col.Item().Line(0.5f);

                    col.Item().PaddingTop(5).AlignCenter().Row(row =>
                    {
                        row.FixedItem(70).Image(qrBytes);
                    });
                    col.Item().AlignCenter().Text("Verifique en efactura.dgi.gub.uy").FontSize(6);
                });
            });
        }).GeneratePdf();
    }

    private static string ObtenerNombreTipo(Cfe cfe) => cfe.Tipo switch
    {
        TipoCfe.ETicket => "e-Ticket",
        TipoCfe.NotaCreditoETicket => "Nota de Crédito e-Ticket",
        TipoCfe.NotaDebitoETicket => "Nota de Débito e-Ticket",
        TipoCfe.EFactura => "e-Factura",
        TipoCfe.NotaCreditoEFactura => "Nota de Crédito e-Factura",
        TipoCfe.NotaDebitoEFactura => "Nota de Débito e-Factura",
        TipoCfe.EFacturaExportacion => "e-Factura Exportación",
        TipoCfe.ERemito => "e-Remito",
        _ => $"CFE Tipo {(int)cfe.Tipo}",
    };

    private static string ObtenerNombreTipoTermico(Cfe cfe) => cfe.Tipo switch
    {
        TipoCfe.ETicket => "e-Ticket",
        TipoCfe.EFactura => "e-Factura",
        TipoCfe.NotaCreditoETicket => "NC e-Ticket",
        TipoCfe.NotaDebitoETicket => "ND e-Ticket",
        _ => $"CFE {(int)cfe.Tipo}",
    };

    private static string ObtenerEtiquetaIva(TipoIva iva) => iva switch
    {
        TipoIva.Exento => "EXE",
        TipoIva.Minimo => "10%",
        TipoIva.Basico => "22%",
        TipoIva.Suspendido => "SUS",
        _ => "N/A",
    };
}
