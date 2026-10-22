using System.Globalization;
using FluentReport;
using FluentReport.Core;
using FluentReport.Styling;
using SkiaSharp;
using ZXing;
using ZXing.QrCode;
using ZXing.Rendering;

namespace UruFactura;

/// <summary>
/// Representación impresa de los CFE emitidos: PDF A4, PDF térmico (ticket de 80 mm) y el código QR,
/// con los datos del pie que exige la DGI (código de seguridad, CAE, rango y vencimiento).
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

    private static string Monto(decimal valor, string formato = "N2") => valor.ToString(formato, Cultura);

    /// <summary>Primeros 6 caracteres del código de seguridad, para imprimir bajo el QR.</summary>
    private static string CodigoSeguridadCorto(CfeEmitido e) => e.CodigoSeguridad[..Math.Min(6, e.CodigoSeguridad.Length)];

    /// <summary>Líneas del pie del comprobante: leyenda del emisor, verificación, CAE, rango y vencimiento.</summary>
    private static List<string> LineasSello(CfeEmitido e)
    {
        var lineas = new List<string>();
        if (!string.IsNullOrWhiteSpace(e.Emisor.FechaIncorporacion))
            lineas.Add($"Fecha emisor: {e.Emisor.FechaIncorporacion}");

        lineas.Add(e.Tipo.Familia() == FamiliaCfe.Ticket && !string.IsNullOrWhiteSpace(e.Emisor.UrlVerificacion)
            ? $"Puede verificar comprobante en {e.Emisor.UrlVerificacion}"
            : "Puede verificar comprobante en www.dgi.gub.uy");

        lineas.Add($"CAE Nº {e.Cae.NumeroAutorizacion}");
        lineas.Add($"Serie {e.Serie}, del {e.Cae.RangoDesde} al {e.Cae.RangoHasta}");
        lineas.Add($"Fecha de vencimiento: {e.Cae.FechaVencimiento:dd/MM/yyyy}");
        return lineas;
    }

    private static byte[] GenerarDocumentoA4(CfeEmitido e, byte[] qrBytes)
    {
        var (cfe, emisor, t) = (e.Documento, e.Emisor, e.Totales);
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
                        row.RelativeItem(3).Column(bloque =>
                        {
                            bloque.Item().Text(emisor.RazonSocial).Bold().FontSize(14);
                            if (!string.IsNullOrWhiteSpace(emisor.NombreComercial))
                                bloque.Item().Text(emisor.NombreComercial).FontSize(10);
                            bloque.Item().Text($"RUT: {emisor.Rut}").FontSize(9);
                            bloque.Item().Text(emisor.DomicilioFiscal).FontSize(9);
                            bloque.Item().Text($"{emisor.Ciudad}, {emisor.Departamento}").FontSize(9);
                        });

                        row.RelativeItem(2).AlignRight().Column(tipoDoc =>
                        {
                            tipoDoc.Item().Border(1).Padding(8).AlignCenter().Column(c =>
                            {
                                c.Item().Text(NombreCompleto(cfe.Tipo)).Bold().FontSize(12);
                                c.Item().Text($"Serie {e.Serie} N° {e.Numero}").Bold().FontSize(11);
                                c.Item().Text($"Fecha: {e.FechaEmision:dd/MM/yyyy}").FontSize(9);
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
                            table.Cell().Padding(4).AlignRight().Text(Monto(linea.Cantidad, "F2")).FontSize(9);
                            table.Cell().Padding(4).Text(linea.NombreItem).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(Monto(linea.PrecioUnitario)).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(ObtenerEtiquetaIva(linea.IndFactIva)).FontSize(9);
                            table.Cell().Padding(4).AlignRight().Text(Monto(linea.MontoTotal)).FontSize(9);
                        }
                    });

                    col.Item().PaddingTop(10).AlignRight().Column(tot =>
                    {
                        if (t.MontoNetoExento > 0)
                            tot.Item().Text($"Exento: {Monto(t.MontoNetoExento)}").FontSize(9);
                        if (t.MontoNetoMinimo > 0)
                        {
                            tot.Item().Text($"Neto IVA 10%: {Monto(t.MontoNetoMinimo)}").FontSize(9);
                            tot.Item().Text($"IVA 10%: {Monto(t.IvaMinimo)}").FontSize(9);
                        }
                        if (t.MontoNetoBasico > 0)
                        {
                            tot.Item().Text($"Neto IVA 22%: {Monto(t.MontoNetoBasico)}").FontSize(9);
                            tot.Item().Text($"IVA 22%: {Monto(t.IvaBasico)}").FontSize(9);
                        }
                        foreach (var d in cfe.DescuentosRecargos)
                            tot.Item().Text($"{(d.Tipo == TipoMovimientoGlobal.Descuento ? "Descuento" : "Recargo")} {d.Glosa}: {Monto(d.Monto)}").FontSize(9);
                        if (cfe.Tipo.Familia() == FamiliaCfe.Resguardo)
                            tot.Item().Text($"TOTAL RETENIDO: {Monto(t.MontoRetenido)}").Bold().FontSize(12);
                        else
                            tot.Item().Text($"TOTAL{(cfe.Moneda == Moneda.PesoUruguayo ? "" : $" ({cfe.Moneda})")}: {Monto(t.MontoTotal)}").Bold().FontSize(12);
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
                            sello.Item().AlignCenter().Text($"Código de seguridad: {CodigoSeguridadCorto(e)}").FontSize(6);
                        });

                        row.RelativeItem().PaddingLeft(10).Column(qrInfo =>
                        {
                            foreach (var linea in LineasSello(e))
                                qrInfo.Item().Text(linea).FontSize(8);
                            if (e.AceptadoPorDgi)
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

    private static byte[] GenerarDocumentoTermico(CfeEmitido e, byte[] qrBytes)
    {
        var (cfe, emisor, t) = (e.Documento, e.Emisor, e.Totales);
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
                    col.Item().AlignCenter().Text($"RUT: {emisor.Rut}").FontSize(7);
                    col.Item().AlignCenter().Text(emisor.DomicilioFiscal).FontSize(7);
                    col.Item().Line(0.5f);

                    col.Item().AlignCenter().Text(NombreCompleto(cfe.Tipo)).Bold().FontSize(8);
                    col.Item().AlignCenter().Text($"Serie {e.Serie} N° {e.Numero}").Bold().FontSize(7);
                    col.Item().AlignCenter().Text($"Fecha: {e.FechaEmision:dd/MM/yyyy}").FontSize(7);
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
                            row.RelativeItem().Text($"  {Monto(linea.Cantidad, "F2")} x {Monto(linea.PrecioUnitario)}").FontSize(7);
                            row.Item().AlignRight().Text(Monto(linea.MontoTotal)).FontSize(7);
                        });
                    }

                    col.Item().Line(0.5f);

                    // Fila "etiqueta ........ monto" del ticket.
                    void Fila(string etiqueta, decimal monto, bool negrita = false) => col.Item().Row(r =>
                    {
                        var izquierda = r.RelativeItem().Text(etiqueta).FontSize(7);
                        var derecha = r.Item().AlignRight().Text(Monto(monto)).FontSize(7);
                        if (negrita)
                        {
                            izquierda.Bold();
                            derecha.Bold();
                        }
                    });

                    if (t.MontoNetoExento > 0)
                        Fila("Exento:", t.MontoNetoExento);
                    if (t.IvaMinimo > 0)
                        Fila("IVA 10%:", t.IvaMinimo);
                    if (t.IvaBasico > 0)
                        Fila("IVA 22%:", t.IvaBasico);
                    foreach (var d in cfe.DescuentosRecargos)
                        Fila(d.Tipo == TipoMovimientoGlobal.Descuento ? $"Desc. {d.Glosa}:" : $"Rec. {d.Glosa}:", d.Monto);
                    Fila("TOTAL:", t.MontoTotal, negrita: true);
                    foreach (var m in cfe.MediosPago.OrderBy(m => m.Orden ?? 0))
                        Fila($"{m.Glosa}:", m.Valor);

                    col.Item().Line(0.5f);

                    col.Item().PaddingTop(5).AlignCenter().Row(row =>
                    {
                        row.FixedItem(70).Image(qrBytes);
                    });
                    col.Item().AlignCenter().Text($"Código de seguridad: {CodigoSeguridadCorto(e)}").FontSize(6);
                    foreach (var linea in LineasSello(e))
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
