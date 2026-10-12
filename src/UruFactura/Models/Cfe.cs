using UruFactura.Enums;

namespace UruFactura.Models;

/// <summary>
/// Modelo principal que representa un CFE (Comprobante Fiscal Electrónico).
/// </summary>
public class Cfe
{
    // --- Identificación ---

    /// <summary>Tipo de comprobante.</summary>
    public TipoCfe Tipo { get; set; }

    /// <summary>Serie del comprobante. Puede ser alfabética o alfanumérica (ej: "A", "1A") desde v25.01.</summary>
    public string? Serie { get; set; }

    /// <summary>Número del comprobante.</summary>
    public long Numero { get; set; }

    /// <summary>
    /// Datos del CAE con el que se emite el comprobante (bloque <c>CAEData</c>).
    /// Los completa <c>UruFacturaClient.AsignarCae</c> (o <c>GenerarXml</c> cuando
    /// <see cref="Numero"/> es 0); también pueden informarse manualmente.
    /// </summary>
    public CaeData? CaeData { get; set; }

    /// <summary>Código de sucursal DGI del emisor (<c>CdgDGISucur</c>).</summary>
    public int CodigoSucursal { get; set; } = 1;

    /// <summary>Fecha de emisión del comprobante.</summary>
    public DateOnly FechaEmision { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    // --- Emisor (completado por SDK desde configuración) ---

    /// <summary>RUT del emisor (sin puntos ni guión, 12 dígitos).</summary>
    public string RutEmisor { get; set; } = string.Empty;

    /// <summary>Razón social del emisor.</summary>
    public string RazonSocialEmisor { get; set; } = string.Empty;

    /// <summary>Nombre comercial del emisor (opcional).</summary>
    public string? NombreComercialEmisor { get; set; }

    /// <summary>Giro o actividad económica del emisor (opcional).</summary>
    public string? Giro { get; set; }

    /// <summary>Domicilio fiscal del emisor.</summary>
    public string DomicilioFiscalEmisor { get; set; } = string.Empty;

    /// <summary>Ciudad del emisor.</summary>
    public string CiudadEmisor { get; set; } = string.Empty;

    /// <summary>Departamento del emisor.</summary>
    public string DepartamentoEmisor { get; set; } = string.Empty;

    // --- Receptor ---

    /// <summary>Datos del receptor del comprobante.</summary>
    public Receptor? Receptor { get; set; }

    /// <summary>
    /// Indicador del motivo del traslado. Obligatorio para e-Remito (181) y e-Remito Despachante (131).
    /// </summary>
    public IndTraslado? IndTraslado { get; set; }

    // --- Exportación ---

    /// <summary>
    /// Cláusula de venta (<c>ClauVenta</c>, Incoterm de 1 a 3 letras, p. ej. "FOB", o "N/A").
    /// Obligatoria en e-Factura de exportación.
    /// </summary>
    public string? ClausulaVenta { get; set; }

    /// <summary>
    /// Modalidad de venta (<c>ModVenta</c>): 1 Régimen general, 2 Consignación, 3 Precio revisable,
    /// 4 Bienes propios a exclaves aduaneros, 90 Régimen general-exportación de servicios,
    /// 91 Exportación mandante, 99 Otras transacciones.
    /// Obligatoria en e-Remito de exportación y en e-Factura de exportación.
    /// </summary>
    public int? ModalidadVenta { get; set; }

    /// <summary>
    /// Vía de transporte (<c>ViaTransp</c>): 1 Marítimo, 2 Aéreo, 3 Terrestre, 9 Otro.
    /// Obligatoria en los comprobantes de exportación.
    /// </summary>
    public int? ViaTransporte { get; set; }

    // --- Condiciones ---

    /// <summary>Forma de pago.</summary>
    public FormaPago FormaPago { get; set; } = FormaPago.Contado;

    /// <summary>Moneda del comprobante.</summary>
    public Moneda Moneda { get; set; } = Moneda.PesoUruguayo;

    /// <summary>Tipo de cambio (si la moneda no es peso uruguayo).</summary>
    public decimal? TipoCambio { get; set; }

    // --- Detalle ---

    /// <summary>Líneas de detalle del comprobante.</summary>
    public List<LineaDetalle> Detalle { get; set; } = [];

    // --- Referencias ---

    /// <summary>Referencias a otros CFE (para notas correctivas).</summary>
    public List<RefCfe> Referencias { get; set; } = [];

    // --- Totales calculados ---

    /// <summary>Monto neto exento de IVA.</summary>
    public decimal MontoNetoExento { get; set; }

    /// <summary>Monto neto con IVA suspendido.</summary>
    public decimal MontoNetoSuspendido { get; set; }

    /// <summary>Monto neto gravado a IVA mínimo (10%).</summary>
    public decimal MontoNetoMinimo { get; set; }

    /// <summary>Monto neto gravado a IVA básico (22%).</summary>
    public decimal MontoNetoBasico { get; set; }

    /// <summary>Monto de IVA mínimo (10% sobre <see cref="MontoNetoMinimo"/>).</summary>
    public decimal IvaMinimo { get; set; }

    /// <summary>Monto de IVA básico (22% sobre <see cref="MontoNetoBasico"/>).</summary>
    public decimal IvaBasico { get; set; }

    /// <summary>Monto neto de exportación y asimiladas (solo tipos de exportación).</summary>
    public decimal MontoExportacion { get; set; }

    /// <summary>Monto total del comprobante (netos + IVA).</summary>
    public decimal MontoTotal { get; set; }

    // --- Estado / firma ---

    /// <summary>XML del CFE sin firmar.</summary>
    public string? XmlSinFirmar { get; set; }

    /// <summary>XML del CFE firmado digitalmente.</summary>
    public string? XmlFirmado { get; set; }

    /// <summary>Número asignado por la DGI al sobre en que se envió este CFE (para consultar su estado).</summary>
    public string? IdReceptorDgi { get; set; }

    /// <summary>Token devuelto por la DGI al recibir el sobre (para consultar su estado).</summary>
    public string? TokenDgi { get; set; }

    /// <summary>Código de respuesta de la DGI.</summary>
    public string? CodigoRespuestaDgi { get; set; }

    /// <summary>Mensaje de respuesta de la DGI.</summary>
    public string? MensajeRespuestaDgi { get; set; }

    /// <summary>
    /// Indica si la DGI aceptó el CFE (estado <c>AE</c>). Solo se actualiza al consultar
    /// el estado del envío o del comprobante; recibir el sobre no implica aceptación.
    /// </summary>
    public bool AceptadoPorDgi { get; set; }

    /// <summary>
    /// Calcula los totales del CFE a partir del detalle.
    /// Tasas: IVA mínimo 10%, IVA básico 22%.
    /// </summary>
    public virtual void CalcularTotales()
    {
        MontoNetoExento = 0;
        MontoNetoSuspendido = 0;
        MontoNetoMinimo = 0;
        MontoNetoBasico = 0;
        MontoExportacion = 0;

        if (EsExportacion(Tipo))
        {
            // Exportación y asimiladas: todo el detalle es IndFact 10, sin IVA.
            IvaMinimo = 0;
            IvaBasico = 0;
            MontoExportacion = Math.Round(Detalle.Sum(l => l.MontoTotal), 2);
            MontoTotal = MontoExportacion;
            return;
        }

        foreach (var linea in Detalle)
        {
            switch (linea.IndFactIva)
            {
                case TipoIva.Exento:
                    MontoNetoExento += linea.MontoTotal;
                    break;
                case TipoIva.Suspendido:
                    MontoNetoSuspendido += linea.MontoTotal;
                    break;
                case TipoIva.Minimo:
                    MontoNetoMinimo += linea.MontoTotal;
                    break;
                case TipoIva.Basico:
                    MontoNetoBasico += linea.MontoTotal;
                    break;
            }
        }

        MontoNetoExento = Math.Round(MontoNetoExento, 2);
        MontoNetoSuspendido = Math.Round(MontoNetoSuspendido, 2);
        MontoNetoMinimo = Math.Round(MontoNetoMinimo, 2);
        MontoNetoBasico = Math.Round(MontoNetoBasico, 2);

        IvaMinimo = Math.Round(MontoNetoMinimo * 0.10m, 2);
        IvaBasico = Math.Round(MontoNetoBasico * 0.22m, 2);

        MontoTotal = MontoNetoExento + MontoNetoSuspendido
                   + MontoNetoMinimo + IvaMinimo
                   + MontoNetoBasico + IvaBasico;
        MontoTotal = Math.Round(MontoTotal, 2);
    }

    private static readonly TipoCfe[] TiposCorreccion =
    [
        TipoCfe.NotaCreditoETicket, TipoCfe.NotaDebitoETicket,
        TipoCfe.NotaCreditoEFactura, TipoCfe.NotaDebitoEFactura,
        TipoCfe.NotaCreditoEFacturaExportacion, TipoCfe.NotaDebitoEFacturaExportacion,
    ];

    private static readonly TipoCfe[] TiposExportacion =
    [
        TipoCfe.EFacturaExportacion, TipoCfe.NotaCreditoEFacturaExportacion,
        TipoCfe.NotaDebitoEFacturaExportacion, TipoCfe.ERemitoExportacion,
    ];

    /// <summary>Indica si el tipo es de exportación (detalle sin IVA, <c>IndFact</c> 10).</summary>
    public static bool EsExportacion(TipoCfe tipo) => Array.IndexOf(TiposExportacion, tipo) >= 0;

    private static readonly TipoCfe[] TiposFactura =
    [
        TipoCfe.EFactura, TipoCfe.NotaCreditoEFactura, TipoCfe.NotaDebitoEFactura,
        TipoCfe.EFacturaExportacion, TipoCfe.NotaCreditoEFacturaExportacion, TipoCfe.NotaDebitoEFacturaExportacion,
    ];

    private static readonly TipoCfe[] TiposRemito =
    [
        TipoCfe.ERemito, TipoCfe.ERemitoExportacion,
    ];

    /// <summary>
    /// Valida el CFE y retorna la lista de errores encontrados.
    /// </summary>
    public virtual IReadOnlyList<string> Validar()
    {
        var errors = new List<string>();

        if (Tipo == TipoCfe.EResguardo)
            errors.Add("El e-Resguardo (182) requiere retenciones/percepciones y aún no está soportado por el modelo.");

        if (Numero is <= 0 or > 9_999_999)
            errors.Add("El número de comprobante debe estar entre 1 y 9999999.");

        if (!SerieValida(Serie))
            errors.Add("La serie es obligatoria y debe ser una letra, dos letras o un dígito 1-9 seguido de una letra (ej: \"A\", \"AB\", \"1A\").");

        if (CaeData is null)
            errors.Add("Faltan los datos del CAE (CaeData). Use client.AsignarCae(cfe) o informe CaeData.");
        else
            errors.AddRange(CaeData.Validar(Numero));

        if (CodigoSucursal is < 1 or > 9999)
            errors.Add("CodigoSucursal debe estar entre 1 y 9999.");

        if (RutEmisor is null || RutEmisor.Length != 12 || !RutEmisor.All(char.IsAsciiDigit))
            errors.Add("RutEmisor debe tener exactamente 12 dígitos.");

        if (string.IsNullOrWhiteSpace(RazonSocialEmisor) || RazonSocialEmisor.Length > 150)
            errors.Add("RazonSocialEmisor es obligatoria (máx. 150 caracteres).");

        if (string.IsNullOrWhiteSpace(DomicilioFiscalEmisor) || DomicilioFiscalEmisor.Length > 70)
            errors.Add("DomicilioFiscalEmisor es obligatorio (máx. 70 caracteres).");

        if (string.IsNullOrWhiteSpace(CiudadEmisor) || CiudadEmisor.Length > 30)
            errors.Add("CiudadEmisor es obligatoria (máx. 30 caracteres).");

        if (string.IsNullOrWhiteSpace(DepartamentoEmisor) || DepartamentoEmisor.Length > 30)
            errors.Add("DepartamentoEmisor es obligatorio (máx. 30 caracteres).");

        if (Detalle.Count > (Tipo == TipoCfe.ETicket || Tipo == TipoCfe.NotaCreditoETicket
                              || Tipo == TipoCfe.NotaDebitoETicket
                              || Tipo == TipoCfe.EFactura || Tipo == TipoCfe.NotaCreditoEFactura
                              || Tipo == TipoCfe.NotaDebitoEFactura ? 700 : 200))
            errors.Add("El CFE excede la cantidad máxima de líneas de detalle permitida para su tipo.");

        if (FechaEmision == default)
            errors.Add("FechaEmision es obligatoria.");

        if (Detalle.Count == 0)
            errors.Add("El CFE debe tener al menos una línea de detalle.");

        for (int i = 0; i < Detalle.Count; i++)
        {
            var linea = Detalle[i];
            if (string.IsNullOrWhiteSpace(linea.NombreItem))
                errors.Add($"Línea {i + 1}: NombreItem es obligatorio.");
            else if (linea.NombreItem.Length > 80)
                errors.Add($"Línea {i + 1}: NombreItem admite hasta 80 caracteres.");
            if (linea.UnidadMedida is { Length: > 4 })
                errors.Add($"Línea {i + 1}: UnidadMedida admite hasta 4 caracteres.");
            if (linea.Cantidad <= 0)
                errors.Add($"Línea {i + 1}: Cantidad debe ser mayor a cero.");
            if (linea.PrecioUnitario < 0)
                errors.Add($"Línea {i + 1}: PrecioUnitario no puede ser negativo.");
        }

        if (Moneda != Moneda.PesoUruguayo && TipoCambio is null or <= 0)
            errors.Add("TipoCambio es obligatorio y debe ser mayor a cero cuando la moneda no es Peso Uruguayo.");

        // Notas de corrección requieren referencias
        if (Array.Exists(TiposCorreccion, t => t == Tipo) && Referencias.Count == 0)
            errors.Add("Las notas de crédito/débito deben referenciar al menos un CFE.");

        // Desde formato CFE v25.01 las referencias en notas correctivas deben informar
        // monto y moneda del comprobante referenciado (campos F-C8 y F-C9, DGI obliga desde 15/04/2026).
        if (Array.Exists(TiposCorreccion, t => t == Tipo))
        {
            for (int i = 0; i < Referencias.Count; i++)
            {
                var refCfe = Referencias[i];
                if (refCfe.MontoCfeRef is null)
                    errors.Add($"Referencia {i + 1}: MontoCfeRef es obligatorio en notas de crédito/débito (CFE v25.01).");
                if (refCfe.MonedaCfeRef is null)
                    errors.Add($"Referencia {i + 1}: MonedaCfeRef es obligatorio en notas de crédito/débito (CFE v25.01).");
            }
        }

        if (EsExportacion(Tipo))
        {
            if (Tipo != TipoCfe.ERemitoExportacion && string.IsNullOrWhiteSpace(ClausulaVenta))
                errors.Add("ClausulaVenta es obligatoria en comprobantes de exportación (ej: \"FOB\" o \"N/A\").");
            if (ViaTransporte is not (1 or 2 or 3 or 9))
                errors.Add("ViaTransporte es obligatoria en exportación (1 Marítimo, 2 Aéreo, 3 Terrestre, 9 Otro).");
            if (ModalidadVenta is null && (Tipo is TipoCfe.EFacturaExportacion or TipoCfe.ERemitoExportacion))
                errors.Add("ModalidadVenta es obligatoria en e-Factura y e-Remito de exportación.");
        }

        // e-Factura y tipos relacionados requieren datos del receptor
        if (Array.Exists(TiposFactura, t => t == Tipo) && Receptor == null)
            errors.Add("Las e-Facturas y e-Resguardos requieren datos del receptor.");

        // e-Remito y e-Remito Despachante requieren IndTraslado válido
        if (Array.Exists(TiposRemito, t => t == Tipo) && IndTraslado == null)
            errors.Add("Los e-Remitos requieren indicar el motivo de traslado (IndTraslado).");
        else if (Array.Exists(TiposRemito, t => t == Tipo) && IndTraslado.HasValue
                 && !Enum.IsDefined(typeof(IndTraslado), IndTraslado.Value))
            errors.Add("IndTraslado contiene un valor no soportado para e-Remitos.");

        return errors;
    }

    private static bool SerieValida(string? serie) =>
        serie is { Length: 1 } && char.IsAsciiLetterUpper(serie[0])
        || serie is { Length: 2 }
           && (char.IsAsciiLetterUpper(serie[0]) && char.IsAsciiLetterUpper(serie[1])
               || serie[0] is >= '1' and <= '9' && char.IsAsciiLetterUpper(serie[1]));
}
