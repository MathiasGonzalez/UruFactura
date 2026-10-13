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

    /// <summary>Número interno asignado por el emisor (<c>NroInterno</c>, hasta 50 caracteres, opcional).</summary>
    public string? NroInterno { get; set; }

    /// <summary>Fecha de vencimiento del pago (<c>FchVenc</c>, opcional; típica en ventas a crédito).</summary>
    public DateOnly? FechaVencimiento { get; set; }

    /// <summary>Información adicional del comprobante (<c>InfoAdicionalDoc</c>, hasta 150 caracteres, opcional).</summary>
    public string? InfoAdicional { get; set; }

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

    /// <summary>Teléfonos del emisor (hasta 2, de hasta 20 caracteres).</summary>
    public List<string> TelefonosEmisor { get; set; } = [];

    /// <summary>Correo electrónico del emisor (opcional).</summary>
    public string? CorreoEmisor { get; set; }

    /// <summary>Nombre o identificación de la sucursal del emisor (<c>EmiSucursal</c>, hasta 20 caracteres, opcional).</summary>
    public string? SucursalEmisor { get; set; }

    /// <summary>Información adicional del emisor (hasta 150 caracteres, opcional).</summary>
    public string? InfoAdicionalEmisor { get; set; }

    // --- Receptor ---

    /// <summary>Datos del receptor del comprobante.</summary>
    public Receptor? Receptor { get; set; }

    /// <summary>
    /// Datos del mandante. Obligatorios en las ventas por cuenta ajena (131-133, 141-143 y sus contingencias).
    /// </summary>
    public Mandante? Mandante { get; set; }

    /// <summary>
    /// Indicador del motivo del traslado. Obligatorio para e-Remito (181) y e-Remito de exportación (124).
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

    /// <summary>
    /// Descuentos y recargos globales (hasta 20). Cambian la base imponible del IVA indicado.
    /// No aplican a remitos ni resguardos.
    /// </summary>
    public List<DescuentoRecargoGlobal> DescuentosRecargos { get; set; } = [];

    /// <summary>Medios de pago (hasta 40). No aplican a remitos ni resguardos.</summary>
    public List<MedioPago> MediosPago { get; set; } = [];

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

    /// <summary>Total retenido/percibido (e-Resguardo): suma de las retenciones de las líneas.</summary>
    public decimal MontoRetenido { get; set; }

    // --- Datos de la firma (se completan al firmar) ---

    /// <summary>Fecha y hora de la firma del CFE (<c>TmstFirma</c>); la completa <c>FirmarCfe</c>.</summary>
    public DateTimeOffset? FechaFirma { get; set; }

    /// <summary>
    /// Código de seguridad del CFE: hash (<c>DigestValue</c>, base64) de la firma. Los primeros 6
    /// caracteres se imprimen bajo el QR. Lo completa <c>FirmarCfe</c>.
    /// </summary>
    public string? CodigoSeguridad { get; set; }

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
    /// Calcula los totales del CFE a partir del detalle y de los descuentos/recargos globales.
    /// Tasas: IVA mínimo 10%, IVA básico 22%. Es idempotente.
    /// </summary>
    public virtual void CalcularTotales()
    {
        MontoNetoExento = 0;
        MontoNetoSuspendido = 0;
        MontoNetoMinimo = 0;
        MontoNetoBasico = 0;
        MontoExportacion = 0;
        IvaMinimo = 0;
        IvaBasico = 0;
        MontoRetenido = 0;

        var familia = Tipo.Familia();

        if (familia == FamiliaCfe.Resguardo)
        {
            MontoRetenido = Math.Round(Detalle.SelectMany(l => l.Retenciones).Sum(r => r.Valor), 2);
            MontoTotal = 0;
            return;
        }

        var ajusteGlobal = DescuentosRecargos
            .Where(d => d.Monto > 0)
            .ToList();

        decimal Ajuste(TipoIva iva) => ajusteGlobal
            .Where(d => d.IndFact == iva)
            .Sum(d => d.Tipo == TipoMovimientoGlobal.Descuento ? -d.Monto : d.Monto);

        if (Tipo.EsExportacion())
        {
            // Exportación y asimiladas: todo el detalle es IndFact 10, sin IVA.
            var neto = Detalle.Sum(l => l.MontoTotal)
                     + ajusteGlobal.Sum(d => d.Tipo == TipoMovimientoGlobal.Descuento ? -d.Monto : d.Monto);
            MontoExportacion = Math.Round(neto, 2);
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

        MontoNetoExento = Math.Round(MontoNetoExento + Ajuste(TipoIva.Exento), 2);
        MontoNetoSuspendido = Math.Round(MontoNetoSuspendido + Ajuste(TipoIva.Suspendido), 2);
        MontoNetoMinimo = Math.Round(MontoNetoMinimo + Ajuste(TipoIva.Minimo), 2);
        MontoNetoBasico = Math.Round(MontoNetoBasico + Ajuste(TipoIva.Basico), 2);

        IvaMinimo = Math.Round(MontoNetoMinimo * 0.10m, 2);
        IvaBasico = Math.Round(MontoNetoBasico * 0.22m, 2);

        MontoTotal = MontoNetoExento + MontoNetoSuspendido
                   + MontoNetoMinimo + IvaMinimo
                   + MontoNetoBasico + IvaBasico;
        MontoTotal = Math.Round(MontoTotal, 2);
    }

    /// <summary>Indica si el tipo es de exportación (detalle sin IVA, <c>IndFact</c> 10).</summary>
    public static bool EsExportacion(TipoCfe tipo) => tipo.EsExportacion();

    /// <summary>
    /// Tope de monto neto (sin IVA, en pesos) a partir del cual un e-Ticket debe identificar al
    /// receptor (5.000 UI según el formato CFE). <c>null</c> desactiva la verificación. Lo completa
    /// el cliente cuando <c>UruFacturaConfig.ValorUnidadIndexada</c> está configurado.
    /// </summary>
    public decimal? TopeMontoNetoSinReceptor { get; set; }

    /// <summary>
    /// Valida el CFE y retorna la lista de errores encontrados.
    /// </summary>
    public virtual IReadOnlyList<string> Validar()
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(Tipo))
        {
            errors.Add($"El tipo de CFE {(int)Tipo} no está soportado.");
            return errors;
        }

        var familia = Tipo.Familia();

        if (Numero is <= 0 or > 9_999_999)
            errors.Add("El número de comprobante debe estar entre 1 y 9999999.");

        if (!SerieValida(Serie))
            errors.Add("La serie es obligatoria y debe ser una letra, dos letras o un dígito 1-9 seguido de una letra (ej: \"A\", \"AB\", \"1A\").");

        if (CaeData is null)
            errors.Add("Faltan los datos del CAE (CaeData). Use client.AsignarCae(cfe) o informe CaeData.");
        else
            errors.AddRange(CaeData.Validar(Numero));

        ValidarEmisor(errors);

        if (FechaEmision == default)
            errors.Add("FechaEmision es obligatoria.");
        else if (FechaVencimiento is { } venc && venc < FechaEmision)
            errors.Add("FechaVencimiento no puede ser anterior a la fecha de emisión.");

        if (NroInterno is { Length: > 50 })
            errors.Add("NroInterno admite hasta 50 caracteres.");
        if (InfoAdicional is { Length: > 150 })
            errors.Add("InfoAdicional admite hasta 150 caracteres.");

        var maxLineas = familia is FamiliaCfe.Ticket or FamiliaCfe.Factura ? 700 : 200;
        if (Detalle.Count == 0)
            errors.Add("El CFE debe tener al menos una línea de detalle.");
        else if (Detalle.Count > maxLineas)
            errors.Add($"El CFE excede la cantidad máxima de líneas de detalle permitida para su tipo ({maxLineas}).");

        if (familia == FamiliaCfe.Resguardo)
            ValidarRetenciones(errors);
        else
            ValidarLineas(errors, familia);

        if (Moneda != Moneda.PesoUruguayo && TipoCambio is null or <= 0)
            errors.Add("TipoCambio es obligatorio y debe ser mayor a cero cuando la moneda no es Peso Uruguayo.");
        else if (TipoCambio is > 9999.999m)
            errors.Add("TipoCambio no puede superar 9999.999.");

        // Notas de corrección requieren referencias
        if (Tipo.EsNotaCorreccion())
        {
            if (Referencias.Count == 0)
                errors.Add("Las notas de crédito/débito deben referenciar al menos un CFE.");

            // Desde formato CFE v25.01 las referencias en notas correctivas deben informar
            // monto y moneda del comprobante referenciado (campos F-C8 y F-C9, DGI obliga desde 15/04/2026).
            for (int i = 0; i < Referencias.Count; i++)
            {
                var refCfe = Referencias[i];
                if (refCfe.MontoCfeRef is null)
                    errors.Add($"Referencia {i + 1}: MontoCfeRef es obligatorio en notas de crédito/débito (CFE v25.01).");
                if (refCfe.MonedaCfeRef is null)
                    errors.Add($"Referencia {i + 1}: MonedaCfeRef es obligatorio en notas de crédito/débito (CFE v25.01).");
            }
        }

        if (Referencias.Count > 40)
            errors.Add("Se admiten hasta 40 referencias.");

        if (Tipo.EsExportacion())
        {
            if (familia == FamiliaCfe.FacturaExportacion && (string.IsNullOrWhiteSpace(ClausulaVenta)
                    || !(ClausulaVenta == "N/A" || ClausulaVenta.Length is >= 1 and <= 3 && ClausulaVenta.All(char.IsAsciiLetter))))
                errors.Add("ClausulaVenta es obligatoria en exportación (1 a 3 letras, ej: \"FOB\", o \"N/A\").");
            if (ViaTransporte is not (1 or 2 or 3 or 9))
                errors.Add("ViaTransporte es obligatoria en exportación (1 Marítimo, 2 Aéreo, 3 Terrestre, 9 Otro).");
            if (ModalidadVenta is not (1 or 2 or 3 or 4 or 80 or 90 or 91 or 99)
                && (familia == FamiliaCfe.RemitoExportacion || Tipo is TipoCfe.EFacturaExportacion or TipoCfe.EFacturaExportacionContingencia))
                errors.Add("ModalidadVenta es obligatoria en e-Factura y e-Remito de exportación (1, 2, 3, 4, 80, 90, 91 o 99).");
        }

        ValidarReceptor(errors, familia);

        if (Tipo.EsRemito())
        {
            if (IndTraslado == null)
                errors.Add("Los e-Remitos requieren indicar el motivo de traslado (IndTraslado).");
            else if (!Enum.IsDefined(typeof(IndTraslado), IndTraslado.Value))
                errors.Add("IndTraslado contiene un valor no soportado para e-Remitos.");
        }

        if (Tipo.EsCuentaAjena())
            ValidarMandante(errors);
        else if (Mandante is not null)
            errors.Add("El mandante solo se informa en las ventas por cuenta ajena.");

        ValidarDescuentosYMedios(errors, familia);

        if (TopeMontoNetoSinReceptor is { } tope && familia == FamiliaCfe.Ticket && !Tipo.EsCuentaAjena())
        {
            CalcularTotales();
            var netoPesos = (MontoNetoExento + MontoNetoSuspendido + MontoNetoMinimo + MontoNetoBasico)
                            * (Moneda == Moneda.PesoUruguayo ? 1m : TipoCambio ?? 1m);
            if (netoPesos > tope && string.IsNullOrWhiteSpace(Receptor?.Documento))
                errors.Add($"El e-Ticket supera el tope de monto neto sin identificar al receptor ({tope:N2} pesos): informe el documento del receptor.");
        }

        return errors;
    }

    private void ValidarEmisor(List<string> errors)
    {
        if (CodigoSucursal is < 1 or > 9999)
            errors.Add("CodigoSucursal debe estar entre 1 y 9999.");

        if (!Rut.EsValido(RutEmisor))
            errors.Add("RutEmisor debe tener 12 dígitos y un dígito verificador válido.");

        if (string.IsNullOrWhiteSpace(RazonSocialEmisor) || RazonSocialEmisor.Length > 150)
            errors.Add("RazonSocialEmisor es obligatoria (máx. 150 caracteres).");

        if (NombreComercialEmisor is { Length: > 30 })
            errors.Add("NombreComercialEmisor admite hasta 30 caracteres.");

        if (Giro is { Length: > 100 })
            errors.Add("Giro admite hasta 100 caracteres.");

        if (string.IsNullOrWhiteSpace(DomicilioFiscalEmisor) || DomicilioFiscalEmisor.Length > 70)
            errors.Add("DomicilioFiscalEmisor es obligatorio (máx. 70 caracteres).");

        if (string.IsNullOrWhiteSpace(CiudadEmisor) || CiudadEmisor.Length > 30)
            errors.Add("CiudadEmisor es obligatoria (máx. 30 caracteres).");

        if (string.IsNullOrWhiteSpace(DepartamentoEmisor) || DepartamentoEmisor.Length > 30)
            errors.Add("DepartamentoEmisor es obligatorio (máx. 30 caracteres).");

        if (TelefonosEmisor.Count > 2 || TelefonosEmisor.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 20))
            errors.Add("TelefonosEmisor admite hasta 2 teléfonos de hasta 20 caracteres.");

        if (CorreoEmisor is { Length: > 80 })
            errors.Add("CorreoEmisor admite hasta 80 caracteres.");

        if (SucursalEmisor is { Length: > 20 })
            errors.Add("SucursalEmisor admite hasta 20 caracteres.");

        if (InfoAdicionalEmisor is { Length: > 150 })
            errors.Add("InfoAdicionalEmisor admite hasta 150 caracteres.");
    }

    private void ValidarLineas(List<string> errors, FamiliaCfe familia)
    {
        var sinPrecio = familia == FamiliaCfe.Remito;
        for (int i = 0; i < Detalle.Count; i++)
        {
            var linea = Detalle[i];
            var n = i + 1;
            if (string.IsNullOrWhiteSpace(linea.NombreItem))
                errors.Add($"Línea {n}: NombreItem es obligatorio.");
            else if (linea.NombreItem.Length > 80)
                errors.Add($"Línea {n}: NombreItem admite hasta 80 caracteres.");
            if (linea.Descripcion is { Length: > 1000 })
                errors.Add($"Línea {n}: Descripcion admite hasta 1000 caracteres.");
            if (linea.UnidadMedida is { Length: > 4 })
                errors.Add($"Línea {n}: UnidadMedida admite hasta 4 caracteres.");
            if (linea.Cantidad <= 0)
                errors.Add($"Línea {n}: Cantidad debe ser mayor a cero.");
            if (!sinPrecio && linea.PrecioUnitario < 0)
                errors.Add($"Línea {n}: PrecioUnitario no puede ser negativo.");
            if (linea.DescuentoPorcentaje is < 0 or > 100)
                errors.Add($"Línea {n}: DescuentoPorcentaje debe estar entre 0 y 100.");
            if (linea.Codigos.Count > 5 || linea.Codigos.Any(c =>
                    string.IsNullOrWhiteSpace(c.Tipo) || c.Tipo.Length > 10
                    || string.IsNullOrWhiteSpace(c.Codigo) || c.Codigo.Length > 35))
                errors.Add($"Línea {n}: Codigos admite hasta 5 códigos (tipo ≤ 10 y código ≤ 35 caracteres).");
            if (!sinPrecio && !Tipo.EsExportacion() && !Enum.IsDefined(linea.IndFactIva))
                errors.Add($"Línea {n}: IndFactIva no es válido.");
            if (linea.Retenciones.Count > 0)
                errors.Add($"Línea {n}: las retenciones/percepciones solo se informan en el e-Resguardo.");
        }
    }

    private void ValidarRetenciones(List<string> errors)
    {
        var total = 0;
        for (int i = 0; i < Detalle.Count; i++)
        {
            var linea = Detalle[i];
            var n = i + 1;
            if (linea.Retenciones.Count is < 1 or > 5)
                errors.Add($"Línea {n}: el e-Resguardo requiere de 1 a 5 retenciones/percepciones por línea.");

            foreach (var r in linea.Retenciones)
            {
                total++;
                if (string.IsNullOrWhiteSpace(r.Codigo) || r.Codigo.Length > 8)
                    errors.Add($"Línea {n}: el código de retención es obligatorio (máx. 8 caracteres).");
                if (r.Valor < 0 || r.MontoSujeto < 0)
                    errors.Add($"Línea {n}: los montos de retención no pueden ser negativos.");
                if (r.Tasa is < 0 or > 100)
                    errors.Add($"Línea {n}: la tasa de retención debe estar entre 0 y 100.");
                if (r.InfoAdicional is { Length: > 150 })
                    errors.Add($"Línea {n}: InfoAdicional de la retención admite hasta 150 caracteres.");
            }
        }

        var codigosDistintos = Detalle.SelectMany(l => l.Retenciones).Select(r => r.Codigo).Distinct().Count();
        if (codigosDistintos > 20)
            errors.Add("El e-Resguardo admite hasta 20 códigos de retención distintos.");
        if (total == 0 && Detalle.Count > 0)
            errors.Add("El e-Resguardo debe informar al menos una retención/percepción.");
    }

    private void ValidarReceptor(List<string> errors, FamiliaCfe familia)
    {
        if (Tipo.RequiereReceptor() && Receptor == null)
        {
            errors.Add(familia == FamiliaCfe.Resguardo
                ? "El e-Resguardo requiere datos del receptor."
                : "Las e-Facturas requieren datos del receptor.");
            return;
        }

        if (Receptor is not { } r)
            return;

        var conDocumento = !string.IsNullOrWhiteSpace(r.Documento);
        var pais = string.IsNullOrWhiteSpace(r.CodigoPais) ? "UY" : r.CodigoPais.ToUpperInvariant();

        if (familia == FamiliaCfe.Factura && conDocumento)
        {
            if (r.TipoDocumento != TipoDocumentoReceptor.Rut || pais != "UY")
                errors.Add("El receptor de una e-Factura debe identificarse con un RUT uruguayo.");
            else if (!Rut.EsValido(r.Documento))
                errors.Add("El RUT del receptor debe tener 12 dígitos y un dígito verificador válido.");
        }
        else if (conDocumento && r.TipoDocumento == TipoDocumentoReceptor.Rut && pais == "UY"
                 && !Rut.EsValido(r.Documento))
            errors.Add("El RUT del receptor debe tener 12 dígitos y un dígito verificador válido.");

        if (conDocumento && (pais.Length != 2 || !pais.All(char.IsAsciiLetterUpper)))
            errors.Add("CodigoPais del receptor debe ser un código ISO de 2 letras.");
        if (conDocumento && r.Documento!.Length > (pais == "UY" ? 12 : 20))
            errors.Add($"El documento del receptor admite hasta {(pais == "UY" ? 12 : 20)} caracteres.");
        if (r.RazonSocial is { Length: > 150 })
            errors.Add("La razón social del receptor admite hasta 150 caracteres.");
        if (familia == FamiliaCfe.Remito && string.IsNullOrWhiteSpace(r.RazonSocial))
            errors.Add("El receptor de un e-Remito requiere razón social.");
        if (familia == FamiliaCfe.Remito && string.IsNullOrWhiteSpace(r.Direccion))
            errors.Add("El receptor de un e-Remito requiere dirección.");
        if (familia == FamiliaCfe.Remito && string.IsNullOrWhiteSpace(r.Ciudad))
            errors.Add("El receptor de un e-Remito requiere ciudad.");
        if (r.Direccion is { Length: > 70 })
            errors.Add("La dirección del receptor admite hasta 70 caracteres.");
        if (r.Ciudad is { Length: > 30 } || r.Departamento is { Length: > 30 } || r.Pais is { Length: > 30 })
            errors.Add("Ciudad, departamento y país del receptor admiten hasta 30 caracteres.");
        if (r.CodigoPostal is not null && !(r.CodigoPostal.Length is >= 1 and <= 5 && r.CodigoPostal.All(char.IsAsciiDigit)))
            errors.Add("CodigoPostal del receptor debe ser numérico de hasta 5 dígitos.");
        if (r.InfoAdicional is { Length: > 150 })
            errors.Add("InfoAdicional del receptor admite hasta 150 caracteres.");
        if (r.CompraId is { Length: > 50 })
            errors.Add("CompraId del receptor admite hasta 50 caracteres.");
        if (r.LugarDestinoEntrega is { Length: > 100 })
            errors.Add("LugarDestinoEntrega admite hasta 100 caracteres.");
    }

    private void ValidarMandante(List<string> errors)
    {
        if (Mandante is not { } m)
        {
            errors.Add("Las ventas por cuenta ajena requieren los datos del mandante (Mandante).");
            return;
        }

        if (string.IsNullOrWhiteSpace(m.Documento) || m.Documento.Length > 20)
            errors.Add("Mandante.Documento es obligatorio (máx. 20 caracteres).");
        if (string.IsNullOrWhiteSpace(m.Nombre) || m.Nombre.Length > 150)
            errors.Add("Mandante.Nombre es obligatorio (máx. 150 caracteres).");
        if (m.CodigoPais is not { Length: 2 } || !m.CodigoPais.All(char.IsAsciiLetterUpper))
            errors.Add("Mandante.CodigoPais debe ser un código ISO de 2 letras.");
    }

    private void ValidarDescuentosYMedios(List<string> errors, FamiliaCfe familia)
    {
        var sinZona = familia is FamiliaCfe.Remito or FamiliaCfe.RemitoExportacion or FamiliaCfe.Resguardo;

        if (sinZona && DescuentosRecargos.Count > 0)
            errors.Add("Los remitos y resguardos no admiten descuentos/recargos globales.");
        if (sinZona && MediosPago.Count > 0)
            errors.Add("Los remitos y resguardos no admiten medios de pago.");

        if (DescuentosRecargos.Count > 20)
            errors.Add("Se admiten hasta 20 descuentos/recargos globales.");
        for (int i = 0; i < DescuentosRecargos.Count; i++)
        {
            var d = DescuentosRecargos[i];
            var n = i + 1;
            if (d.Monto <= 0)
                errors.Add($"Descuento/recargo {n}: el monto debe ser mayor a cero.");
            if (string.IsNullOrWhiteSpace(d.Glosa) || d.Glosa.Length > 100)
                errors.Add($"Descuento/recargo {n}: la glosa es obligatoria (máx. 100 caracteres).");
            if (d.Codigo is < 0 or > 999)
                errors.Add($"Descuento/recargo {n}: el código debe tener hasta 3 dígitos.");
            if (!Enum.IsDefined(d.IndFact))
                errors.Add($"Descuento/recargo {n}: IndFact no es válido.");
        }

        if (MediosPago.Count > 40)
            errors.Add("Se admiten hasta 40 medios de pago.");
        for (int i = 0; i < MediosPago.Count; i++)
        {
            var m = MediosPago[i];
            var n = i + 1;
            if (string.IsNullOrWhiteSpace(m.Glosa) || m.Glosa.Length > 100)
                errors.Add($"Medio de pago {n}: la glosa es obligatoria (máx. 100 caracteres).");
            if (m.Codigo is < 0 or > 999)
                errors.Add($"Medio de pago {n}: el código debe tener hasta 3 dígitos.");
            if (m.Orden is < 0 or > 99)
                errors.Add($"Medio de pago {n}: el orden debe tener hasta 2 dígitos.");
        }
    }

    private static bool SerieValida(string? serie) =>
        serie is { Length: 1 } && char.IsAsciiLetterUpper(serie[0])
        || serie is { Length: 2 }
           && (char.IsAsciiLetterUpper(serie[0]) && char.IsAsciiLetterUpper(serie[1])
               || serie[0] is >= '1' and <= '9' && char.IsAsciiLetterUpper(serie[1]));
}
