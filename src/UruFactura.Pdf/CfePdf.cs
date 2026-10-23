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

    // -----------------------------------------------------------------------
    // Contenido (común a los dos formatos)
    // -----------------------------------------------------------------------

    private static string Monto(decimal valor) => valor.ToString("N2", Cultura);

    /// <summary>Cantidad con los decimales que tiene (hasta 3, como en el XML).</summary>
    private static string Cantidad(decimal valor) => valor.ToString("#,0.###", Cultura);

    /// <summary>Precio unitario con al menos 2 decimales y hasta 6, como en el XML.</summary>
    private static string Precio(decimal valor) => valor.ToString("#,0.00####", Cultura);

    private static string Porcentaje(decimal tasa) => $"{tasa.ToString("0.###", Cultura)}%";

    /// <summary>Columna del detalle: título, ancho (relativo o fijo, en puntos) y alineación.</summary>
    internal sealed record Columna(string Titulo, float Ancho, bool AnchoFijo = false, bool Derecha = true);

    /// <summary>
    /// Columnas del detalle según el tipo: el e-Remito no lleva precios, la exportación no lleva IVA y
    /// el e-Resguardo informa retenciones/percepciones en lugar de ítems.
    /// </summary>
    internal static Columna[] Columnas(TipoCfe tipo) => tipo.Familia() switch
    {
        FamiliaCfe.Remito => [new("Cant.", 50, AnchoFijo: true), new("Descripción", 1, Derecha: false)],
        FamiliaCfe.FacturaExportacion or FamiliaCfe.RemitoExportacion =>
            [new("Cant.", 40, AnchoFijo: true), new("Descripción", 5, Derecha: false), new("P.Unit.", 2), new("Total", 2)],
        FamiliaCfe.Resguardo => [new("Código", 2, Derecha: false), new("Tasa", 1), new("Monto sujeto", 2), new("Valor", 2)],
        _ => [new("Cant.", 40, AnchoFijo: true), new("Descripción", 5, Derecha: false), new("P.Unit.", 2), new("IVA", 1), new("Total", 2)],
    };

    /// <summary>Filas del detalle, con las columnas de <see cref="Columnas"/>.</summary>
    internal static IEnumerable<string[]> Filas(Cfe cfe) => cfe.Tipo.Familia() switch
    {
        FamiliaCfe.Remito => cfe.Detalle.Select(l => new[] { Cantidad(l.Cantidad), l.NombreItem }),
        FamiliaCfe.FacturaExportacion or FamiliaCfe.RemitoExportacion => cfe.Detalle.Select(l =>
            new[] { Cantidad(l.Cantidad), l.NombreItem, Precio(l.PrecioUnitario), Monto(l.MontoTotal) }),
        FamiliaCfe.Resguardo => cfe.Detalle.SelectMany(l => l.Retenciones).Select(r =>
            new[] { r.Codigo, r.Tasa is { } tasa ? Porcentaje(tasa) : "", Monto(r.MontoSujeto), Monto(r.Valor) }),
        _ => cfe.Detalle.Select(l =>
            new[] { Cantidad(l.Cantidad), l.NombreItem, Precio(l.PrecioUnitario), EtiquetaIva(l.IndFactIva), Monto(l.MontoTotal) }),
    };

    /// <summary>Indicador de IVA de una línea de e-Ticket o e-Factura.</summary>
    internal static string EtiquetaIva(TipoIva iva) => iva switch
    {
        TipoIva.Exento => "Exento",
        TipoIva.Minimo => Porcentaje(Totales.TasaIvaMinima),
        TipoIva.Basico => Porcentaje(Totales.TasaIvaBasica),
        TipoIva.Suspendido => "Susp.",
        _ => "",
    };

    /// <summary>Ajustes globales, montos netos e IVA que se imprimen antes del total (ninguno en remitos y resguardos).</summary>
    internal static IEnumerable<(string Etiqueta, decimal Monto)> LineasTotales(CfeEmitido e)
    {
        var (cfe, t) = (e.Documento, e.Totales);
        if (cfe.Tipo.EsRemito() || cfe.Tipo.Familia() == FamiliaCfe.Resguardo)
            yield break;

        foreach (var d in cfe.DescuentosRecargos)
            yield return ($"{(d.Tipo == TipoMovimientoGlobal.Descuento ? "Descuento" : "Recargo")} {d.Glosa}", d.Monto);

        if (cfe.Tipo.EsExportacion())
            yield break;

        if (t.MontoNetoExento > 0)
            yield return ("Exento", t.MontoNetoExento);
        if (t.MontoNetoSuspendido > 0)
            yield return ("IVA en suspenso", t.MontoNetoSuspendido);
        if (t.MontoNetoMinimo > 0)
        {
            yield return ($"Neto IVA {Porcentaje(Totales.TasaIvaMinima)}", t.MontoNetoMinimo);
            yield return ($"IVA {Porcentaje(Totales.TasaIvaMinima)}", t.IvaMinimo);
        }
        if (t.MontoNetoBasico > 0)
        {
            yield return ($"Neto IVA {Porcentaje(Totales.TasaIvaBasica)}", t.MontoNetoBasico);
            yield return ($"IVA {Porcentaje(Totales.TasaIvaBasica)}", t.IvaBasico);
        }
    }

    /// <summary>Total del comprobante con su moneda (el e-Remito no tiene).</summary>
    internal static (string Etiqueta, decimal Monto)? Total(CfeEmitido e)
    {
        var moneda = e.Documento.Moneda == Moneda.PesoUruguayo ? "" : $" ({e.Documento.Moneda.CodigoIso()})";
        return e.Tipo.Familia() switch
        {
            FamiliaCfe.Remito => null,
            FamiliaCfe.Resguardo => ($"TOTAL RETENIDO{moneda}", e.Totales.MontoRetenido),
            _ => ($"TOTAL{moneda}", e.Totales.MontoTotal),
        };
    }

    /// <summary>Moneda y tipo de cambio, si no es el peso uruguayo.</summary>
    internal static string? LineaMoneda(Cfe cfe) =>
        cfe.Moneda == Moneda.PesoUruguayo || cfe.Tipo.Familia() == FamiliaCfe.Remito
            ? null
            : $"Moneda: {cfe.Moneda.CodigoIso()}" + (cfe.TipoCambio is { } tc ? $" - Tipo de cambio: {tc.ToString("N3", Cultura)}" : "");

    /// <summary>Los CFE a los que refiere (en las notas, el comprobante que corrigen).</summary>
    internal static IEnumerable<string> LineasReferencias(Cfe cfe) => cfe.Referencias.Select(r =>
        $"{NombreCompleto(r.TipoCfe)} {r.Serie}-{r.NroCfe} del {r.FechaCfe.ToString("dd/MM/yyyy", Cultura)}"
        + (r.MontoCfeRef is { } monto ? $" por {(r.MonedaCfeRef is { } m ? m.CodigoIso() + " " : "")}{Monto(monto)}" : "")
        + (string.IsNullOrWhiteSpace(r.Razon) ? "" : $" - {r.Razon}"));

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
        lineas.Add($"Fecha de vencimiento: {e.Cae.FechaVencimiento.ToString("dd/MM/yyyy", Cultura)}");
        return lineas;
    }

    // -----------------------------------------------------------------------
    // Formatos
    // -----------------------------------------------------------------------

    private static byte[] GenerarDocumentoA4(CfeEmitido e, byte[] qrBytes)
    {
        var (cfe, emisor) = (e.Documento, e.Emisor);
        var columnas = Columnas(cfe.Tipo);
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
                                c.Item().Text($"Fecha: {e.FechaEmision.ToString("dd/MM/yyyy", Cultura)}").FontSize(9);
                            });
                        });
                    });

                    col.Item().Line(1, "#9E9E9E");
                });

                page.Content().Column(col =>
                {
                    if (cfe.Receptor is { } receptor)
                    {
                        col.Item().PaddingVertical(5).Column(rec =>
                        {
                            rec.Item().Text("Receptor:").Bold().FontSize(9);
                            if (!string.IsNullOrWhiteSpace(receptor.RazonSocial))
                                rec.Item().Text(receptor.RazonSocial).FontSize(9);
                            if (!string.IsNullOrWhiteSpace(receptor.Documento))
                                rec.Item().Text($"RUT/Doc: {receptor.Documento}").FontSize(9);
                            if (!string.IsNullOrWhiteSpace(receptor.Direccion))
                                rec.Item().Text(receptor.Direccion).FontSize(9);
                            var lugar = string.Join(", ", new[] { receptor.Ciudad, receptor.Departamento, receptor.Pais }
                                .Where(x => !string.IsNullOrWhiteSpace(x)));
                            if (lugar.Length > 0)
                                rec.Item().Text(lugar).FontSize(9);
                        });

                        col.Item().Line(0.5f, "#F5F5F5");
                    }

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

                        foreach (var fila in Filas(cfe))
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
                        foreach (var (etiqueta, monto) in LineasTotales(e))
                            tot.Item().Text($"{etiqueta}: {Monto(monto)}").FontSize(9);
                        if (Total(e) is { } total)
                            tot.Item().Text($"{total.Etiqueta}: {Monto(total.Monto)}").Bold().FontSize(12);
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

                    if (cfe.Referencias.Count > 0)
                    {
                        col.Item().PaddingTop(8).Column(rf =>
                        {
                            rf.Item().Text("Referencias:").Bold().FontSize(9);
                            foreach (var referencia in LineasReferencias(cfe))
                                rf.Item().Text(referencia).FontSize(9);
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
                    col.Item().AlignCenter().Text($"Fecha: {e.FechaEmision.ToString("dd/MM/yyyy", Cultura)}").FontSize(7);
                    col.Item().Line(0.5f);

                    if (cfe.Receptor is { } receptor && (receptor.RazonSocial ?? receptor.Documento) is { } cliente)
                    {
                        col.Item().Text($"Cliente: {cliente}").FontSize(7);
                        col.Item().Line(0.5f);
                    }

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
                                Fila(linea.NombreItem, Cantidad(linea.Cantidad));
                            break;

                        case FamiliaCfe.Resguardo:
                            foreach (var r in cfe.Detalle.SelectMany(l => l.Retenciones))
                                Fila($"{r.Codigo}{(r.Tasa is { } tasa ? $" {Porcentaje(tasa)}" : "")} sobre {Monto(r.MontoSujeto)}", Monto(r.Valor));
                            break;

                        default:
                            foreach (var linea in cfe.Detalle)
                            {
                                col.Item().Text(linea.NombreItem).FontSize(7);
                                var iva = cfe.Tipo.EsExportacion() ? "" : $" ({EtiquetaIva(linea.IndFactIva)})";
                                Fila($"  {Cantidad(linea.Cantidad)} x {Precio(linea.PrecioUnitario)}{iva}", Monto(linea.MontoTotal));
                            }
                            break;
                    }

                    col.Item().Line(0.5f);

                    foreach (var (etiqueta, monto) in LineasTotales(e))
                        Fila($"{etiqueta}:", Monto(monto));
                    if (Total(e) is { } total)
                        Fila($"{total.Etiqueta}:", Monto(total.Monto), negrita: true);
                    foreach (var m in cfe.MediosPago.OrderBy(m => m.Orden ?? 0))
                        Fila($"{m.Glosa}:", Monto(m.Valor));

                    if (cfe.Referencias.Count > 0)
                    {
                        col.Item().Line(0.5f);
                        foreach (var referencia in LineasReferencias(cfe))
                            col.Item().Text($"Ref.: {referencia}").FontSize(6);
                    }

                    if (cfe.Mandante is { } mandante)
                        col.Item().Text($"Por cuenta de: {mandante.Nombre} ({mandante.Documento})").FontSize(6);

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
}
