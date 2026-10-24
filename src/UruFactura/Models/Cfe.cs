using UruFactura.Formatting;

namespace UruFactura;

/// <summary>
/// Contenido de un CFE (Comprobante Fiscal Electrónico) a emitir. Es inmutable y
/// <see cref="IUruFacturaClient.EmitirAsync"/> copia sus listas: lo emitido es exactamente lo que queda
/// firmado aunque después se modifiquen las listas originales. La numeración, el CAE y los datos del
/// emisor los completa la emisión.
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
    /// precisión con que se escriben en el XML (IVA a las tasas de <see cref="Totales.TasaIvaMinima"/> y
    /// <see cref="Totales.TasaIvaBasica"/>).
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
        var ivaMinimo = CfeFormat.Redondear(minimo * Totales.TasaIvaMinima / 100m);
        var ivaBasico = CfeFormat.Redondear(basico * Totales.TasaIvaBasica / 100m);

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
    /// Valida el contenido del comprobante (sin numeración ni emisor, que valida la emisión) con las
    /// restricciones del esquema de la DGI: lo que pasa esta validación produce un XML válido.
    /// </summary>
    /// <param name="hoy">Fecha actual en Uruguay (la de emisión si <see cref="FechaEmision"/> se omite).</param>
    /// <param name="topeMontoNetoSinReceptor">Tope en pesos a partir del cual un e-Ticket debe identificar al receptor (5.000 UI).</param>
    internal List<ErrorValidacion> Validar(DateOnly hoy, decimal? topeMontoNetoSinReceptor)
    {
        var v = new Validacion();

        if (!Enum.IsDefined(Tipo))
        {
            v.Error(nameof(Tipo), $"El tipo de CFE {(int)Tipo} no está soportado.");
            return v.Errores;
        }

        var familia = Tipo.Familia();
        var fecha = FechaEmision ?? hoy;

        if (fecha > hoy)
            v.Error(nameof(FechaEmision), "No puede ser posterior a la fecha actual.");
        else
            v.Fecha(nameof(FechaEmision), fecha);
        if (FechaVencimiento is { } venc && venc < fecha)
            v.Error(nameof(FechaVencimiento), "No puede ser anterior a la fecha de emisión.");
        else if (FechaVencimiento > Validacion.FechaMaxima)
            v.Error(nameof(FechaVencimiento), "Debe ser hasta el 31/12/2050.");
        v.Texto(nameof(NroInterno), NroInterno, 50);
        v.Texto(nameof(InfoAdicional), InfoAdicional, 150);

        var maxLineas = familia is FamiliaCfe.Ticket or FamiliaCfe.Factura ? 700 : 200;
        if (Detalle.Count == 0)
            v.Error(nameof(Detalle), "Debe tener al menos una línea.");
        else if (Detalle.Count > maxLineas)
            v.Error(nameof(Detalle), $"Admite hasta {maxLineas} líneas en este tipo de comprobante.");

        if (familia == FamiliaCfe.Resguardo)
            ValidarRetenciones(v);
        else
            ValidarLineas(v, familia);

        if (!Enum.IsDefined(Moneda))
            v.Error(nameof(Moneda), "No es válida.");
        if (!Enum.IsDefined(FormaPago))
            v.Error(nameof(FormaPago), "No es válida.");
        if (Moneda != Moneda.PesoUruguayo)
        {
            if (TipoCambio is { } tipoCambio)
                v.TipoCambio(nameof(TipoCambio), tipoCambio);
            else
                v.Error(nameof(TipoCambio), "Es obligatorio cuando la moneda no es el peso uruguayo.");
        }

        ValidarReferencias(v, fecha);

        if (Tipo.EsExportacion())
            ValidarExportacion(v, familia);

        ValidarReceptor(v, familia);

        if (Tipo.EsRemito())
        {
            if (IndTraslado is null)
                v.Error(nameof(IndTraslado), "Es obligatorio en los e-Remitos (motivo del traslado).");
            else if (!Enum.IsDefined(IndTraslado.Value))
                v.Error(nameof(IndTraslado), "No es un motivo de traslado válido.");
        }

        if (Tipo.EsCuentaAjena())
            ValidarMandante(v);
        else if (Mandante is not null)
            v.Error(nameof(Mandante), "Solo se informa en las ventas por cuenta ajena.");

        ValidarDescuentosYMedios(v, familia);

        // Los totales se calculan con el resto válido: con montos fuera de rango podrían desbordar.
        if (v.Errores.Count == 0)
            ValidarTotales(v, familia, topeMontoNetoSinReceptor);

        return v.Errores;
    }

    private void ValidarLineas(Validacion v, FamiliaCfe familia)
    {
        var remito = Tipo.EsRemito();
        var conPrecio = familia != FamiliaCfe.Remito;
        for (int i = 0; i < Detalle.Count; i++)
        {
            var linea = Detalle[i];
            var c = $"{nameof(Detalle)}[{i}]";
            v.Texto($"{c}.NombreItem", linea.NombreItem, 80, obligatorio: true);
            v.Texto($"{c}.Descripcion", linea.Descripcion, 1000);
            v.Texto($"{c}.UnidadMedida", linea.UnidadMedida, 4);

            var errores = v.Errores.Count;
            if (CfeFormat.Redondear(linea.Cantidad, 3) <= 0)
                v.Error($"{c}.Cantidad", "Debe ser mayor a cero (se informa con 3 decimales).");
            else if (linea.Cantidad > Validacion.CantidadMaxima)
                v.Error($"{c}.Cantidad", "Supera el máximo del formato CFE (14 dígitos enteros).");
            if (conPrecio && linea.PrecioUnitario < 0)
                v.Error($"{c}.PrecioUnitario", "No puede ser negativo.");
            else if (conPrecio && linea.PrecioUnitario > Validacion.PrecioMaximo)
                v.Error($"{c}.PrecioUnitario", "Supera el máximo del formato CFE (11 dígitos enteros).");

            if (remito)
            {
                // Item_Rem y Item_Rem_Exp no informan descuentos ni recargos.
                if (linea.DescuentoMonto != 0 || linea.DescuentoPorcentaje is not (null or 0) || linea.RecargoMonto != 0)
                    v.Error(c, "Los remitos no admiten descuentos ni recargos por línea.");
            }
            else
            {
                v.Monto($"{c}.DescuentoMonto", linea.DescuentoMonto);
                v.Monto($"{c}.RecargoMonto", linea.RecargoMonto);
                if (linea.DescuentoPorcentaje is < 0 or > 100)
                    v.Error($"{c}.DescuentoPorcentaje", "Debe estar entre 0 y 100.");
            }

            // Con cantidad, precio y ajustes en rango, el monto de la línea no puede desbordar.
            if (conPrecio && v.Errores.Count == errores)
            {
                if (linea.DescuentoEfectivo > linea.Bruto)
                    v.Error($"{c}.DescuentoMonto", "No puede superar Cantidad × PrecioUnitario.");
                else if (linea.MontoTotal > Validacion.MontoMaximo)
                    v.Error(c, "El monto de la línea supera el máximo del formato CFE (15 dígitos enteros).");
            }

            if (linea.Codigos.Count > 5)
                v.Error($"{c}.Codigos", "Admite hasta 5 códigos.");
            for (int j = 0; j < linea.Codigos.Count; j++)
            {
                v.Texto($"{c}.Codigos[{j}].Tipo", linea.Codigos[j].Tipo, 10, obligatorio: true);
                v.Texto($"{c}.Codigos[{j}].Codigo", linea.Codigos[j].Codigo, 35, obligatorio: true);
            }

            if (!remito && !Tipo.EsExportacion() && !Enum.IsDefined(linea.IndFactIva))
                v.Error($"{c}.IndFactIva", "No es válido.");
            if (linea.Retenciones.Count > 0)
                v.Error($"{c}.Retenciones", "Solo se informan en el e-Resguardo.");
        }
    }

    private void ValidarRetenciones(Validacion v)
    {
        for (int i = 0; i < Detalle.Count; i++)
        {
            var linea = Detalle[i];
            var c = $"{nameof(Detalle)}[{i}]";
            if (linea.Retenciones.Count is < 1 or > 5)
                v.Error($"{c}.Retenciones", "El e-Resguardo requiere de 1 a 5 retenciones/percepciones por línea.");

            for (int j = 0; j < linea.Retenciones.Count; j++)
            {
                var r = linea.Retenciones[j];
                var cr = $"{c}.Retenciones[{j}]";
                v.Texto($"{cr}.Codigo", r.Codigo, 8, obligatorio: true);
                v.Monto($"{cr}.MontoSujeto", r.MontoSujeto);
                v.Monto($"{cr}.Valor", r.Valor);
                if (r.Tasa is < 0 or > 100)
                    v.Error($"{cr}.Tasa", "Debe estar entre 0 y 100.");
                v.Texto($"{cr}.InfoAdicional", r.InfoAdicional, 150);
            }
        }

        if (Detalle.SelectMany(l => l.Retenciones).Select(r => r.Codigo).Distinct().Count() > 20)
            v.Error(nameof(Detalle), "El e-Resguardo admite hasta 20 códigos de retención distintos.");
    }

    private void ValidarReferencias(Validacion v, DateOnly fechaEmision)
    {
        var nota = Tipo.EsNotaCorreccion();
        if (nota && Referencias.Count == 0)
            v.Error(nameof(Referencias), "Las notas de crédito y débito deben referenciar al menos un CFE.");
        if (Referencias.Count > 40)
            v.Error(nameof(Referencias), "Admite hasta 40 referencias.");

        for (int i = 0; i < Referencias.Count; i++)
        {
            var r = Referencias[i];
            var c = $"{nameof(Referencias)}[{i}]";
            if (!Enum.IsDefined(r.TipoCfe))
                v.Error($"{c}.TipoCfe", "No es un tipo de CFE soportado.");
            if (!Validacion.EsSerie(r.Serie))
                v.Error($"{c}.Serie", "Debe ser una letra, dos letras o un dígito 1-9 y una letra (ej: \"A\").");
            if (r.NroCfe is < 1 or > 9_999_999)
                v.Error($"{c}.NroCfe", "Debe estar entre 1 y 9999999.");
            if (r.FechaCfe > fechaEmision)
                v.Error($"{c}.FechaCfe", "No puede ser posterior a la fecha de emisión.");
            else
                v.Fecha($"{c}.FechaCfe", r.FechaCfe);
            v.Texto($"{c}.Razon", r.Razon, 90);

            // Desde el formato CFE v25.01 las notas informan monto y moneda del CFE referenciado (F-C8 y F-C9).
            if (r.MontoCfeRef is { } monto)
                v.Monto($"{c}.MontoCfeRef", monto);
            else if (nota)
                v.Error($"{c}.MontoCfeRef", "Es obligatorio en las notas de crédito y débito (formato CFE v25.01).");
            if (r.MonedaCfeRef is { } moneda && !Enum.IsDefined(moneda))
                v.Error($"{c}.MonedaCfeRef", "No es válida.");
            else if (r.MonedaCfeRef is null && nota)
                v.Error($"{c}.MonedaCfeRef", "Es obligatoria en las notas de crédito y débito (formato CFE v25.01).");
            if (r.TipoCambioCfeRef is { } tipoCambio)
                v.TipoCambio($"{c}.TipoCambioCfeRef", tipoCambio);
        }
    }

    private void ValidarExportacion(Validacion v, FamiliaCfe familia)
    {
        var clausulaValida = ClausulaVenta is "N/A"
            || ClausulaVenta is { Length: >= 1 and <= 3 } && ClausulaVenta.All(char.IsAsciiLetter);
        if (familia == FamiliaCfe.FacturaExportacion ? !clausulaValida : !string.IsNullOrWhiteSpace(ClausulaVenta) && !clausulaValida)
            v.Error(nameof(ClausulaVenta), "Es obligatoria en la e-Factura de exportación: de 1 a 3 letras (ej: \"FOB\") o \"N/A\".");
        if (ViaTransporte is not (1 or 2 or 3 or 9))
            v.Error(nameof(ViaTransporte), "Es obligatoria en exportación: 1 Marítimo, 2 Aéreo, 3 Terrestre o 9 Otro.");

        var modalidadObligatoria = familia == FamiliaCfe.RemitoExportacion
            || Tipo is TipoCfe.EFacturaExportacion or TipoCfe.EFacturaExportacionContingencia;
        if (ModalidadVenta is not (1 or 2 or 3 or 4 or 80 or 90 or 91 or 99) && (modalidadObligatoria || ModalidadVenta is not null))
            v.Error(nameof(ModalidadVenta), "Debe ser 1, 2, 3, 4, 80, 90, 91 o 99 (obligatoria en la e-Factura y el e-Remito de exportación).");
    }

    private void ValidarReceptor(Validacion v, FamiliaCfe familia)
    {
        if (Receptor is not { } r)
        {
            if (Tipo.RequiereReceptor())
                v.Error(nameof(Receptor), familia == FamiliaCfe.Resguardo
                    ? "El e-Resguardo requiere los datos del receptor."
                    : "Las e-Facturas requieren los datos del receptor.");
            return;
        }

        const string c = nameof(Receptor);
        var pais = string.IsNullOrWhiteSpace(r.CodigoPais) ? "UY" : r.CodigoPais.ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(r.Documento))
        {
            if (familia == FamiliaCfe.Factura && (r.TipoDocumento != TipoDocumentoReceptor.Rut || pais != "UY"))
                v.Error($"{c}.TipoDocumento", "El receptor de una e-Factura se identifica con un RUT uruguayo.");
            else if (r.TipoDocumento == TipoDocumentoReceptor.Rut && pais == "UY" && !Rut.EsValido(r.Documento))
                v.Error($"{c}.Documento", "El RUT debe tener 12 dígitos y un dígito verificador válido.");
            if (!Enum.IsDefined(r.TipoDocumento))
                v.Error($"{c}.TipoDocumento", "No es válido.");
            v.CodigoPais($"{c}.CodigoPais", pais);
            v.Texto($"{c}.Documento", r.Documento, pais == "UY" ? 12 : 20);
        }
        else if (familia == FamiliaCfe.Factura)
            v.Error($"{c}.Documento", "La e-Factura requiere el RUT del receptor.");

        v.Texto($"{c}.RazonSocial", r.RazonSocial, 150, obligatorio: familia == FamiliaCfe.Remito);
        v.Texto($"{c}.Direccion", r.Direccion, 70, obligatorio: familia == FamiliaCfe.Remito);
        v.Texto($"{c}.Ciudad", r.Ciudad, 30, obligatorio: familia == FamiliaCfe.Remito);
        v.Texto($"{c}.Departamento", r.Departamento, 30);
        v.Texto($"{c}.Pais", r.Pais, 30);
        if (r.CodigoPostal is not null && !(r.CodigoPostal.Length is >= 1 and <= 5 && r.CodigoPostal.All(char.IsAsciiDigit)))
            v.Error($"{c}.CodigoPostal", "Debe ser numérico, de hasta 5 dígitos.");
        v.Texto($"{c}.InfoAdicional", r.InfoAdicional, 150);
        v.Texto($"{c}.CompraId", r.CompraId, 50);
        v.Texto($"{c}.LugarDestinoEntrega", r.LugarDestinoEntrega, 100);
    }

    private void ValidarMandante(Validacion v)
    {
        if (Mandante is not { } m)
        {
            v.Error(nameof(Mandante), "Las ventas por cuenta ajena requieren los datos del mandante.");
            return;
        }

        v.Texto("Mandante.Documento", m.Documento, 20, obligatorio: true);
        v.Texto("Mandante.Nombre", m.Nombre, 150, obligatorio: true);
        v.CodigoPais("Mandante.CodigoPais", m.CodigoPais?.ToUpperInvariant());
        if (!Enum.IsDefined(m.TipoDocumento))
            v.Error("Mandante.TipoDocumento", "No es válido.");
    }

    private void ValidarDescuentosYMedios(Validacion v, FamiliaCfe familia)
    {
        var sinZona = familia is FamiliaCfe.Remito or FamiliaCfe.RemitoExportacion or FamiliaCfe.Resguardo;

        if (sinZona && DescuentosRecargos.Count > 0)
            v.Error(nameof(DescuentosRecargos), "Los remitos y resguardos no admiten descuentos ni recargos globales.");
        if (sinZona && MediosPago.Count > 0)
            v.Error(nameof(MediosPago), "Los remitos y resguardos no admiten medios de pago.");

        if (DescuentosRecargos.Count > 20)
            v.Error(nameof(DescuentosRecargos), "Admite hasta 20 descuentos o recargos globales.");
        for (int i = 0; i < DescuentosRecargos.Count; i++)
        {
            var d = DescuentosRecargos[i];
            var c = $"{nameof(DescuentosRecargos)}[{i}]";
            if (d.Monto <= 0)
                v.Error($"{c}.Monto", "Debe ser mayor a cero.");
            else
                v.Monto($"{c}.Monto", d.Monto);
            v.Texto($"{c}.Glosa", d.Glosa, 100, obligatorio: true);
            if (d.Codigo is < 0 or > 999)
                v.Error($"{c}.Codigo", "Debe tener hasta 3 dígitos.");
            if (!Enum.IsDefined(d.Tipo))
                v.Error($"{c}.Tipo", "No es válido.");
            if (!Enum.IsDefined(d.IndFact))
                v.Error($"{c}.IndFact", "No es válido.");
        }

        if (MediosPago.Count > 40)
            v.Error(nameof(MediosPago), "Admite hasta 40 medios de pago.");
        for (int i = 0; i < MediosPago.Count; i++)
        {
            var m = MediosPago[i];
            var c = $"{nameof(MediosPago)}[{i}]";
            v.Texto($"{c}.Glosa", m.Glosa, 100, obligatorio: true);
            if (m.Codigo is < 0 or > 999)
                v.Error($"{c}.Codigo", "Debe tener hasta 3 dígitos.");
            if (m.Orden is < 0 or > 99)
                v.Error($"{c}.Orden", "Debe tener hasta 2 dígitos.");
            v.Monto($"{c}.Valor", m.Valor, admiteNegativo: true);
        }
    }

    private void ValidarTotales(Validacion v, FamiliaCfe familia, decimal? topeMontoNetoSinReceptor)
    {
        var t = CalcularTotales();

        if (familia == FamiliaCfe.Resguardo)
        {
            if (t.MontoRetenido > Validacion.MontoMaximo)
                v.Error(nameof(Detalle), "El total retenido supera el máximo del formato CFE (15 dígitos enteros).");
            return;
        }

        // Las líneas no pueden quedar negativas: solo un descuento global puede dejar una base negativa.
        if (t.MontoTotal < 0 || t.MontoNetoExento < 0 || t.MontoNetoSuspendido < 0 || t.MontoNetoMinimo < 0 || t.MontoNetoBasico < 0)
            v.Error(nameof(DescuentosRecargos), "Los descuentos globales superan la base de su tipo de IVA.");
        else if (t.MontoTotal > Validacion.MontoMaximo)
            v.Error(nameof(Detalle), "El total del comprobante supera el máximo del formato CFE (15 dígitos enteros).");

        if (topeMontoNetoSinReceptor is { } tope && familia == FamiliaCfe.Ticket && !Tipo.EsCuentaAjena()
            && string.IsNullOrWhiteSpace(Receptor?.Documento))
        {
            var netoPesos = t.MontoNeto * (Moneda == Moneda.PesoUruguayo ? 1m : TipoCambio ?? 1m);
            if (netoPesos > tope)
                v.Error($"{nameof(Receptor)}.Documento",
                    $"El e-Ticket supera el tope de monto neto sin identificar al receptor ({CfeFormat.MonetaryPdf(tope)} pesos): informe su documento.");
        }
    }
}
