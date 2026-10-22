using UruFactura.Formatting;

namespace UruFactura;

/// <summary>
/// Contenido de un CFE (Comprobante Fiscal Electrónico) a emitir. Es inmutable: lo que se emite con
/// <see cref="IUruFacturaClient.EmitirAsync"/> es exactamente lo que queda firmado. La numeración, el
/// CAE y los datos del emisor los completa la emisión.
/// </summary>
/// <param name="Tipo">Tipo de comprobante.</param>
public sealed record Cfe(TipoCfe Tipo)
{
    // --- Identificación ---

    /// <summary>Fecha de emisión. Si se omite, es la fecha de Uruguay al momento de emitir.</summary>
    public DateOnly? FechaEmision { get; init; }

    /// <summary>Número interno del emisor (<c>NroInterno</c>, hasta 50 caracteres, opcional).</summary>
    public string? NroInterno { get; init; }

    /// <summary>Fecha de vencimiento del pago (<c>FchVenc</c>, opcional; típica en ventas a crédito).</summary>
    public DateOnly? FechaVencimiento { get; init; }

    /// <summary>Información adicional del comprobante (<c>InfoAdicionalDoc</c>, hasta 150 caracteres, opcional).</summary>
    public string? InfoAdicional { get; init; }

    /// <summary>Datos del emisor para este comprobante. Si se omiten, se usan los de la configuración.</summary>
    public Emisor? Emisor { get; init; }

    // --- Receptor y operación ---

    /// <summary>Receptor. Obligatorio en e-Factura, e-Factura de exportación y e-Resguardo.</summary>
    public Receptor? Receptor { get; init; }

    /// <summary>Mandante. Obligatorio en las ventas por cuenta ajena (131-133, 141-143 y sus contingencias).</summary>
    public Mandante? Mandante { get; init; }

    /// <summary>Motivo del traslado. Obligatorio en e-Remito y e-Remito de exportación.</summary>
    public IndTraslado? IndTraslado { get; init; }

    /// <summary>
    /// Cláusula de venta (<c>ClauVenta</c>, Incoterm de 1 a 3 letras, p. ej. "FOB", o "N/A").
    /// Obligatoria en e-Factura de exportación.
    /// </summary>
    public string? ClausulaVenta { get; init; }

    /// <summary>
    /// Modalidad de venta (<c>ModVenta</c>): 1 Régimen general, 2 Consignación, 3 Precio revisable,
    /// 4 Bienes propios a exclaves aduaneros, 80, 90 Régimen general-exportación de servicios,
    /// 91 Exportación mandante, 99 Otras. Obligatoria en e-Factura y e-Remito de exportación.
    /// </summary>
    public int? ModalidadVenta { get; init; }

    /// <summary>Vía de transporte (<c>ViaTransp</c>): 1 Marítimo, 2 Aéreo, 3 Terrestre, 9 Otro. Obligatoria en exportación.</summary>
    public int? ViaTransporte { get; init; }

    /// <summary>Forma de pago.</summary>
    public FormaPago FormaPago { get; init; } = FormaPago.Contado;

    /// <summary>Moneda del comprobante.</summary>
    public Moneda Moneda { get; init; } = Moneda.PesoUruguayo;

    /// <summary>Tipo de cambio (obligatorio si la moneda no es el peso uruguayo).</summary>
    public decimal? TipoCambio { get; init; }

    // --- Detalle ---

    /// <summary>Líneas de detalle (al menos una).</summary>
    public IReadOnlyList<LineaDetalle> Detalle { get; init; } = [];

    /// <summary>Descuentos y recargos globales (hasta 20). No aplican a remitos ni resguardos.</summary>
    public IReadOnlyList<DescuentoRecargoGlobal> DescuentosRecargos { get; init; } = [];

    /// <summary>Medios de pago (hasta 40). No aplican a remitos ni resguardos.</summary>
    public IReadOnlyList<MedioPago> MediosPago { get; init; } = [];

    /// <summary>Referencias a otros CFE (obligatorias en las notas de crédito y débito, hasta 40).</summary>
    public IReadOnlyList<RefCfe> Referencias { get; init; } = [];

    /// <summary>
    /// Calcula los totales a partir del detalle y de los descuentos/recargos globales, con la misma
    /// precisión con que se escriben en el XML (IVA mínimo 10%, básico 22%).
    /// </summary>
    public Totales CalcularTotales()
    {
        if (Tipo.Familia() == FamiliaCfe.Resguardo)
            // Cada retención se escribe con 2 decimales: el total es la suma de los valores escritos.
            return new Totales { MontoRetenido = Detalle.SelectMany(l => l.Retenciones).Sum(r => CfeFormat.Redondear(r.Valor)) };

        // ValorDR se escribe con 2 decimales: el ajuste usa ese mismo valor.
        var ajustes = DescuentosRecargos.Where(d => d.Monto > 0).ToList();
        static decimal Ajuste(DescuentoRecargoGlobal d) =>
            d.Tipo == TipoMovimientoGlobal.Descuento ? -CfeFormat.Redondear(d.Monto) : CfeFormat.Redondear(d.Monto);

        if (Tipo.EsExportacion())
        {
            // Exportación y asimiladas: todo el detalle es IndFact 10, sin IVA.
            var exportacion = CfeFormat.Redondear(Detalle.Sum(l => l.MontoTotal) + ajustes.Sum(Ajuste));
            return new Totales { MontoExportacion = exportacion, MontoTotal = exportacion };
        }

        decimal Neto(TipoIva iva) => CfeFormat.Redondear(
            Detalle.Where(l => l.IndFactIva == iva).Sum(l => l.MontoTotal)
            + ajustes.Where(d => d.IndFact == iva).Sum(Ajuste));

        var exento = Neto(TipoIva.Exento);
        var suspendido = Neto(TipoIva.Suspendido);
        var minimo = Neto(TipoIva.Minimo);
        var basico = Neto(TipoIva.Basico);
        var ivaMinimo = CfeFormat.Redondear(minimo * 0.10m);
        var ivaBasico = CfeFormat.Redondear(basico * 0.22m);

        return new Totales
        {
            MontoNetoExento = exento,
            MontoNetoSuspendido = suspendido,
            MontoNetoMinimo = minimo,
            MontoNetoBasico = basico,
            IvaMinimo = ivaMinimo,
            IvaBasico = ivaBasico,
            // Todos los sumandos ya tienen 2 decimales.
            MontoTotal = exento + suspendido + minimo + ivaMinimo + basico + ivaBasico,
        };
    }

    /// <summary>
    /// Valida el contenido del comprobante (sin numeración ni emisor, que valida la emisión).
    /// </summary>
    /// <param name="fechaEmision">Fecha de emisión efectiva.</param>
    /// <param name="topeMontoNetoSinReceptor">Tope en pesos a partir del cual un e-Ticket debe identificar al receptor (5.000 UI).</param>
    internal List<string> Validar(DateOnly fechaEmision, decimal? topeMontoNetoSinReceptor)
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(Tipo))
        {
            errors.Add($"El tipo de CFE {(int)Tipo} no está soportado.");
            return errors;
        }

        var familia = Tipo.Familia();

        if (FechaVencimiento is { } venc && venc < fechaEmision)
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

        if (!Enum.IsDefined(Moneda))
            errors.Add("Moneda no es válida.");
        if (!Enum.IsDefined(FormaPago))
            errors.Add("FormaPago no es válida.");

        if (Moneda != Moneda.PesoUruguayo && TipoCambio is null or <= 0)
            errors.Add("TipoCambio es obligatorio y debe ser mayor a cero cuando la moneda no es Peso Uruguayo.");
        else if (TipoCambio is > 9999.999m)
            errors.Add("TipoCambio no puede superar 9999.999.");

        if (Tipo.EsNotaCorreccion())
        {
            if (Referencias.Count == 0)
                errors.Add("Las notas de crédito/débito deben referenciar al menos un CFE.");

            // Desde el formato CFE v25.01 las referencias de las notas informan monto y moneda (F-C8 y F-C9).
            for (int i = 0; i < Referencias.Count; i++)
            {
                if (Referencias[i].MontoCfeRef is null)
                    errors.Add($"Referencia {i + 1}: MontoCfeRef es obligatorio en notas de crédito/débito (CFE v25.01).");
                if (Referencias[i].MonedaCfeRef is null)
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
            if (IndTraslado is null)
                errors.Add("Los e-Remitos requieren indicar el motivo de traslado (IndTraslado).");
            else if (!Enum.IsDefined(IndTraslado.Value))
                errors.Add("IndTraslado contiene un valor no soportado para e-Remitos.");
        }

        if (Tipo.EsCuentaAjena())
            ValidarMandante(errors);
        else if (Mandante is not null)
            errors.Add("El mandante solo se informa en las ventas por cuenta ajena.");

        ValidarDescuentosYMedios(errors, familia);

        if (topeMontoNetoSinReceptor is { } tope && familia == FamiliaCfe.Ticket && !Tipo.EsCuentaAjena()
            && string.IsNullOrWhiteSpace(Receptor?.Documento))
        {
            var netoPesos = CalcularTotales().MontoNeto * (Moneda == Moneda.PesoUruguayo ? 1m : TipoCambio ?? 1m);
            if (netoPesos > tope)
                errors.Add($"El e-Ticket supera el tope de monto neto sin identificar al receptor ({tope:N2} pesos): informe el documento del receptor.");
        }

        return errors;
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

        if (Detalle.SelectMany(l => l.Retenciones).Select(r => r.Codigo).Distinct().Count() > 20)
            errors.Add("El e-Resguardo admite hasta 20 códigos de retención distintos.");
        if (total == 0 && Detalle.Count > 0)
            errors.Add("El e-Resguardo debe informar al menos una retención/percepción.");
    }

    private void ValidarReceptor(List<string> errors, FamiliaCfe familia)
    {
        if (Tipo.RequiereReceptor() && Receptor is null)
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
        else if (conDocumento && r.TipoDocumento == TipoDocumentoReceptor.Rut && pais == "UY" && !Rut.EsValido(r.Documento))
            errors.Add("El RUT del receptor debe tener 12 dígitos y un dígito verificador válido.");

        if (conDocumento && (pais.Length != 2 || !pais.All(char.IsAsciiLetterUpper)))
            errors.Add("CodigoPais del receptor debe ser un código ISO de 2 letras.");
        if (conDocumento && r.Documento!.Length > (pais == "UY" ? 12 : 20))
            errors.Add($"El documento del receptor admite hasta {(pais == "UY" ? 12 : 20)} caracteres.");
        if (r.RazonSocial is { Length: > 150 })
            errors.Add("La razón social del receptor admite hasta 150 caracteres.");
        if (familia == FamiliaCfe.Remito)
        {
            if (string.IsNullOrWhiteSpace(r.RazonSocial))
                errors.Add("El receptor de un e-Remito requiere razón social.");
            if (string.IsNullOrWhiteSpace(r.Direccion))
                errors.Add("El receptor de un e-Remito requiere dirección.");
            if (string.IsNullOrWhiteSpace(r.Ciudad))
                errors.Add("El receptor de un e-Remito requiere ciudad.");
        }
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
}
