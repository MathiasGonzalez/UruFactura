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
    /// <summary>Días posteriores a hoy hasta los que la DGI admite la fecha de un comprobante (A-C5 y F-C7).</summary>
    internal const int DiasFechaPosterior = 60;

    // --- Identificación ---

    /// <summary>
    /// Fecha de emisión. Si se omite, es la fecha de Uruguay al momento de emitir. Puede ser posterior a hoy hasta
    /// 60 días (como admite la DGI).
    /// </summary>
    public DateOnly? FechaEmision { get; init; }

    /// <summary>Número interno del emisor (<c>NroInterno</c>, hasta 50 caracteres, opcional).</summary>
    public string? NroInterno { get; init; }

    /// <summary>
    /// Período facturado (<c>PeriodoDesde</c> y <c>PeriodoHasta</c>, opcionales), por ejemplo en un servicio
    /// mensual. En e-Tickets, e-Facturas, exportación y e-Boletas de entrada.
    /// </summary>
    public DateOnly? PeriodoDesde { get; init; }

    /// <summary>Fin del período facturado (no anterior a <see cref="PeriodoDesde"/>).</summary>
    public DateOnly? PeriodoHasta { get; init; }

    /// <summary>
    /// Fecha valor del e-Resguardo (<c>FchValor</c>): solo para los organismos del Estado, cuando la fecha de la
    /// retención o percepción no coincide con la del comprobante. Su mes no puede ser posterior al de emisión.
    /// </summary>
    public DateOnly? FechaValor { get; init; }

    /// <summary>Fecha de vencimiento del pago (<c>FchVenc</c>, opcional; típica en ventas a crédito).</summary>
    public DateOnly? FechaVencimiento { get; init; }

    /// <summary>Información adicional del comprobante (<c>InfoAdicionalDoc</c>, hasta 150 caracteres, opcional).</summary>
    public string? InfoAdicional { get; init; }

    /// <summary>
    /// Operación amparada por el secreto profesional (<c>SecProf</c>): la identificación del receptor no es
    /// obligatoria (si se omite, va en la <see cref="Adenda"/>) y, en las ventas por cuenta ajena, tampoco el
    /// mandante.
    /// </summary>
    public bool SecretoProfesional { get; init; }

    /// <summary>
    /// Los montos corresponden a pagos por cuenta de terceros, es decir, a reintegros de gastos
    /// (<c>IndPagCta3ros</c>). En e-Tickets, e-Facturas y exportación; la representación impresa lleva la leyenda
    /// "Pagos por cuenta de terceros" y el reporte diario los informa aparte.
    /// </summary>
    public bool PagoCuentaTerceros { get; init; }

    /// <summary>
    /// Cobranza propia o adelanto de precios (<c>IndCobPropia</c>), solo en e-Tickets y e-Facturas (no en sus
    /// notas): las líneas son no facturables (indicadores 6 o 7), sin descuentos ni recargos globales, y las
    /// referencias indican los comprobantes que se cobran. Se imprime la leyenda "Cobranza".
    /// </summary>
    public bool CobranzaPropia { get; init; }

    /// <summary>
    /// e-Boleta de entrada: compra de moneda extranjera para su reventa (<c>IndCompraMEReventa</c>). Sus líneas van
    /// con el indicador <see cref="IndicadorFacturacion.CompraMonedaExtranjera"/>.
    /// </summary>
    public CompraMonedaExtranjera? CompraMonedaExtranjera { get; init; }

    /// <summary>
    /// Las líneas se expresan en montos brutos (<c>MntBruto</c>): con IVA incluido (e-Ticket y e-Factura) o con
    /// IMEBA y adicionales incluidos (e-Boleta de entrada). Si se omite son netos; el 3 de IVA mínimo o Monotributo
    /// lo informa la emisión según el CAE.
    /// </summary>
    public MontosBrutos? MontosBrutos { get; init; }

    /// <summary>Datos del emisor para este comprobante. Si se omiten, se usan los de la configuración.</summary>
    public Emisor? Emisor { get; init; }

    // --- Receptor y operación ---

    /// <summary>
    /// Receptor (en la e-Boleta de entrada, el vendedor). Obligatorio, salvo con secreto profesional, en
    /// e-Factura, exportación, e-Remito, e-Resguardo y e-Boleta; en el e-Ticket, cuando supera el tope de UI o
    /// documenta retenciones.
    /// </summary>
    public Receptor? Receptor { get; init; }

    /// <summary>
    /// Mandante. Obligatorio en las ventas por cuenta ajena (131-133, 141-143 y sus contingencias), salvo con
    /// secreto profesional, y en la compra de moneda extranjera por cuenta ajena de la e-Boleta de entrada.
    /// </summary>
    public Mandante? Mandante { get; init; }

    /// <summary>Motivo del traslado. Obligatorio en e-Remito y e-Remito de exportación.</summary>
    public IndTraslado? IndTraslado { get; init; }

    /// <summary>
    /// Propietario de la mercadería que traslada un e-Remito o e-Remito de exportación, si no es del emisor
    /// (bienes de terceros).
    /// </summary>
    public Propietario? Propietario { get; init; }

    /// <summary>
    /// Cláusula de venta (<c>ClauVenta</c>, Incoterm de 1 a 3 letras, p. ej. "FOB", o "N/A").
    /// Obligatoria en e-Factura de exportación.
    /// </summary>
    public string? ClausulaVenta { get; init; }

    /// <summary>
    /// Modalidad de venta (<c>ModVenta</c>): 1 Régimen general-exportación de bienes, 2 Consignación, 3 Precio
    /// revisable, 4 Bienes propios a exclaves aduaneros, 90 Régimen general-exportación de servicios, 91 Exportación
    /// mandante o 99 Otras, en exportación (obligatoria en la e-Factura y el e-Remito de exportación); 90
    /// (exportación de servicios) en e-Facturas y e-Tickets, y 80 (régimen Tax Free) en e-Tickets.
    /// </summary>
    public int? ModalidadVenta { get; init; }

    /// <summary>Vía de transporte (<c>ViaTransp</c>): 1 Marítimo, 2 Aéreo, 3 Terrestre, 8 No aplica, 9 Otro. Obligatoria en exportación.</summary>
    public int? ViaTransporte { get; init; }

    /// <summary>Forma de pago.</summary>
    public FormaPago FormaPago { get; init; } = FormaPago.Contado;

    /// <summary>Moneda del comprobante.</summary>
    public Moneda Moneda { get; init; } = Moneda.PesoUruguayo;

    /// <summary>Tipo de cambio (obligatorio si la moneda no es el peso uruguayo).</summary>
    public decimal? TipoCambio { get; init; }

    // --- Detalle ---

    /// <summary>Líneas de detalle (al menos una; hasta 700 en los e-Tickets y 200 en los demás).</summary>
    public IReadOnlyList<LineaDetalle> Detalle { get; init; } = [];

    /// <summary>Subtotales informativos (hasta 20): no cambian los totales.</summary>
    public IReadOnlyList<SubtotalInformativo> SubtotalesInformativos { get; init; } = [];

    /// <summary>Descuentos y recargos globales (hasta 20). No aplican a remitos ni resguardos.</summary>
    public IReadOnlyList<DescuentoRecargoGlobal> DescuentosRecargos { get; init; } = [];

    /// <summary>Medios de pago (hasta 40). No aplican a remitos ni resguardos.</summary>
    public IReadOnlyList<MedioPago> MediosPago { get; init; } = [];

    /// <summary>
    /// Referencias a otros CFE (hasta 40): obligatorias en las notas de crédito y débito, en las cobranzas propias
    /// y en los remitos y resguardos que rebajan o anulan ítems de otro.
    /// </summary>
    public IReadOnlyList<RefCfe> Referencias { get; init; } = [];

    /// <summary>
    /// IVA a otra tasa o sobre fictos (<c>MntIVAOtra</c>), en la moneda del comprobante. Obligatorio si hay
    /// líneas <see cref="IndicadorFacturacion.OtraTasa"/>: el formato no fija esa tasa, así que no se calcula.
    /// </summary>
    public decimal? IvaOtraTasa { get; init; }

    /// <summary>
    /// Adenda: información comercial para el receptor (texto libre; la DGI recomienda el formato
    /// etiqueta-valor). No se firma ni se envía a la DGI: va en el sobre al receptor electrónico y en la
    /// representación impresa, donde es obligatoria si contiene leyendas que exige la normativa o, con secreto
    /// profesional, la identificación del receptor.
    /// </summary>
    public string? Adenda { get; init; }

    /// <summary>
    /// Calcula los totales a partir del detalle y de los descuentos/recargos globales, con la misma
    /// precisión con que se escriben en el XML (IVA a las tasas de <see cref="Totales.TasaIvaMinima"/> y
    /// <see cref="Totales.TasaIvaBasica"/>; el de otra tasa es <see cref="IvaOtraTasa"/>).
    /// </summary>
    public Totales CalcularTotales()
    {
        var familia = Tipo.Familia();
        var retenciones = RetencionesPorCodigo();
        var retenido = retenciones.Where(r => !EsCreditoFiscal(r.Codigo)).Sum(r => r.Valor);
        var creditoFiscal = retenciones.Where(r => EsCreditoFiscal(r.Codigo)).Sum(r => r.Valor);

        if (familia == FamiliaCfe.Resguardo)
            return new Totales { MontoRetenido = retenido, MontoCreditoFiscal = creditoFiscal };
        if (familia == FamiliaCfe.Remito)
            return new Totales(); // el e-Remito no tiene montos
        if (familia == FamiliaCfe.RemitoExportacion)
        {
            // Los ítems que rebajan otro e-Remito restan (A-C113).
            var exportacion = Detalle.Sum(l => l.IndFact == IndicadorFacturacion.RebajaRemito ? -l.MontoTotal : l.MontoTotal);
            return new Totales { MontoExportacion = exportacion, MontoTotal = exportacion, MontoPagar = exportacion };
        }

        // MontoItem y ValorDR se escriben con 2 decimales: las sumas usan esos mismos valores.
        var lineas = Detalle.GroupBy(l => IndicadorInformado(l.IndFact)).ToDictionary(g => g.Key, g => g.Sum(l => l.MontoTotal));
        decimal Lineas(IndicadorFacturacion indicador) => lineas.GetValueOrDefault(indicador);
        decimal Base(IndicadorFacturacion indicador) => Lineas(indicador) + DescuentosRecargos
            .Where(d => IndicadorInformado(d.IndFact) == indicador)
            .Sum(d => d.Tipo == TipoMovimientoGlobal.Descuento ? -d.MontoEfectivo(Lineas(indicador)) : d.MontoEfectivo(Lineas(indicador)));

        // Los no facturables negativos (7) restan, con sus descuentos y recargos invertidos.
        var noFacturable = Base(IndicadorFacturacion.NoFacturable) - Base(IndicadorFacturacion.NoFacturableNegativo);

        if (familia == FamiliaCfe.FacturaExportacion)
        {
            var exportacion = Base(IndicadorFacturacion.ExportacionYAsimiladas);
            return new Totales
            {
                MontoExportacion = exportacion, MontoTotal = exportacion, MontoNoFacturable = noFacturable,
                MontoPagar = exportacion + noFacturable,
            };
        }

        if (familia == FamiliaCfe.Boleta)
        {
            var noGravadoBoleta = Base(IndicadorFacturacion.VendedorNoContribuyente) + Base(IndicadorFacturacion.VendedorIvaMinimo)
                + Base(IndicadorFacturacion.VendedorImeba) + Base(IndicadorFacturacion.CompraMonedaExtranjera);
            var suspendidoBoleta = Base(IndicadorFacturacion.IvaEnSuspenso);
            var totalBoleta = noGravadoBoleta + suspendidoBoleta;
            return new Totales
            {
                MontoNoGravado = noGravadoBoleta,
                MontoNetoSuspendido = suspendidoBoleta,
                MontoTotal = totalBoleta,
                MontoRetenido = retenido,
                MontoNoFacturable = noFacturable,
                // Con montos que incluyen IMEBA, lo retenido resta del total a pagar (A-C130).
                MontoPagar = totalBoleta + (MontosBrutos == UruFactura.MontosBrutos.ImebaYAdicionalesIncluidos ? -retenido : retenido) + noFacturable,
            };
        }

        var noGravado = Base(IndicadorFacturacion.Exento) + Base(IndicadorFacturacion.IvaMinimoMonotributo);
        var asimiladas = Base(IndicadorFacturacion.ExportacionYAsimiladas);
        var percibido = Base(IndicadorFacturacion.ImpuestoPercibido);
        var suspendido = Base(IndicadorFacturacion.IvaEnSuspenso);
        var minimo = Base(IndicadorFacturacion.TasaMinima);
        var basico = Base(IndicadorFacturacion.TasaBasica);
        var otraTasa = Base(IndicadorFacturacion.OtraTasa);
        if (MontosBrutos == UruFactura.MontosBrutos.IvaIncluido)
        {
            // Con IVA incluido, los netos de cada tasa son la base dividida por (1 + tasa) (A-C116 y A-C117).
            minimo = CfeFormat.Redondear(minimo / (1m + Totales.TasaIvaMinima / 100m));
            basico = CfeFormat.Redondear(basico / (1m + Totales.TasaIvaBasica / 100m));
        }
        var ivaMinimo = CfeFormat.Redondear(minimo * Totales.TasaIvaMinima / 100m);
        var ivaBasico = CfeFormat.Redondear(basico * Totales.TasaIvaBasica / 100m);
        var ivaOtraTasa = CfeFormat.Redondear(IvaOtraTasa ?? 0m);
        // Todos los sumandos ya tienen 2 decimales.
        var total = noGravado + asimiladas + percibido + suspendido + minimo + basico + otraTasa + ivaMinimo + ivaBasico + ivaOtraTasa;

        return new Totales
        {
            MontoNoGravado = noGravado,
            MontoExportacion = asimiladas,
            MontoImpuestoPercibido = percibido,
            MontoNetoSuspendido = suspendido,
            MontoNetoMinimo = minimo,
            MontoNetoBasico = basico,
            MontoNetoOtraTasa = otraTasa,
            IvaMinimo = ivaMinimo,
            IvaBasico = ivaBasico,
            IvaOtraTasa = ivaOtraTasa,
            MontoTotal = total,
            MontoRetenido = retenido,
            MontoCreditoFiscal = creditoFiscal,
            MontoNoFacturable = noFacturable,
            MontoPagar = total + retenido + noFacturable,
        };
    }

    /// <summary>
    /// Indicador de facturación con que se informa una línea o un descuento/recargo: en la e-Factura de
    /// exportación, todo lo que no es entrega gratuita ni no facturable es exportación y asimiladas; en el
    /// e-Remito de exportación, todo lo que no es de valor cero ni rebaja otro remito.
    /// </summary>
    public IndicadorFacturacion IndicadorInformado(IndicadorFacturacion indicador) => Tipo.Familia() switch
    {
        FamiliaCfe.RemitoExportacion when indicador is not (IndicadorFacturacion.EntregaGratuita or IndicadorFacturacion.RebajaRemito)
            => IndicadorFacturacion.ExportacionYAsimiladas,
        FamiliaCfe.FacturaExportacion when indicador is not (IndicadorFacturacion.EntregaGratuita
            or IndicadorFacturacion.NoFacturable or IndicadorFacturacion.NoFacturableNegativo) => IndicadorFacturacion.ExportacionYAsimiladas,
        _ => indicador,
    };

    /// <summary>
    /// El <c>IndFact</c> que se escribe para una línea, o <c>null</c> si su tipo no lo lleva: el e-Remito solo informa
    /// los ítems a rebajar (8), el e-Remito de exportación también los de valor cero (5) y el e-Resguardo los ítems a
    /// anular (9).
    /// </summary>
    internal IndicadorFacturacion? IndicadorEscrito(LineaDetalle linea) => Tipo.Familia() switch
    {
        FamiliaCfe.Remito => linea.IndFact == IndicadorFacturacion.RebajaRemito ? linea.IndFact : null,
        FamiliaCfe.RemitoExportacion => linea.IndFact is IndicadorFacturacion.EntregaGratuita or IndicadorFacturacion.RebajaRemito
            ? linea.IndFact
            : null,
        FamiliaCfe.Resguardo => linea.IndFact == IndicadorFacturacion.AnulacionResguardo ? linea.IndFact : null,
        _ => IndicadorInformado(linea.IndFact),
    };

    /// <summary>
    /// Monto con que se informa un descuento o recargo global (<c>ValorDR</c>, con 2 decimales): su
    /// <see cref="DescuentoRecargoGlobal.Monto"/> o su porcentaje de la suma de las líneas con su indicador.
    /// </summary>
    public decimal MontoInformado(DescuentoRecargoGlobal descuentoRecargo)
    {
        ArgumentNullException.ThrowIfNull(descuentoRecargo);
        var indicador = IndicadorInformado(descuentoRecargo.IndFact);
        return descuentoRecargo.MontoEfectivo(Detalle.Where(l => IndicadorInformado(l.IndFact) == indicador).Sum(l => l.MontoTotal));
    }

    /// <summary>
    /// Retenciones, percepciones y créditos fiscales acumulados por código, en el orden en que aparecen
    /// (<c>RetencPercep</c> de los totales, A-C127 y A-C128). Cada valor se escribe con 2 decimales y, en el
    /// e-Resguardo, los de los ítems a anular restan.
    /// </summary>
    public IReadOnlyList<TotalRetencion> RetencionesPorCodigo()
    {
        var resguardo = Tipo.Familia() == FamiliaCfe.Resguardo;
        return
        [
            .. Detalle
                .SelectMany(l => l.Retenciones.Select(r => (Codigo: r.Codigo ?? "", Valor: resguardo && l.IndFact == IndicadorFacturacion.AnulacionResguardo
                    ? -CfeFormat.Redondear(r.Valor)
                    : CfeFormat.Redondear(r.Valor))))
                .GroupBy(r => r.Codigo, StringComparer.Ordinal)
                .Select(g => new TotalRetencion(g.Key, g.Sum(r => r.Valor))),
        ];
    }

    /// <summary>Código de crédito fiscal (formulario 2181): va a <c>MntTotCredFisc</c> y no al total retenido.</summary>
    internal static bool EsCreditoFiscal(string codigo) => codigo.StartsWith("2181", StringComparison.Ordinal);

    /// <summary>
    /// Usa el indicador de IVA mínimo o Monotributo (16): el CFE (salvo una venta por cuenta ajena) informa
    /// <c>MntBruto</c> 3 y no admite líneas exentas ni gravadas.
    /// </summary>
    internal bool UsaIvaMinimoMonotributo =>
        Tipo.Familia() is FamiliaCfe.Ticket or FamiliaCfe.Factura
        && (Detalle.Any(l => l.IndFact == IndicadorFacturacion.IvaMinimoMonotributo)
            || DescuentosRecargos.Any(d => d.IndFact == IndicadorFacturacion.IvaMinimoMonotributo));

    /// <summary>
    /// Valor del secreto profesional que se informa (<c>SecProf</c>): 2 en las ventas por cuenta ajena, 1 en las
    /// demás; <c>null</c> si la operación no está amparada.
    /// </summary>
    internal int? SecretoProfesionalInformado => SecretoProfesional ? Tipo.EsCuentaAjena() ? 2 : 1 : null;

    /// <summary>Indicadores que no admite un CFE con <c>MntBruto</c> 3 (IVA mínimo o Monotributo).</summary>
    private static readonly HashSet<IndicadorFacturacion> IndicadoresConIva =
    [
        IndicadorFacturacion.Exento, IndicadorFacturacion.TasaMinima, IndicadorFacturacion.TasaBasica,
        IndicadorFacturacion.OtraTasa, IndicadorFacturacion.ImpuestoPercibido, IndicadorFacturacion.IvaEnSuspenso,
    ];

    /// <summary>Indicadores de las líneas de e-Tickets y e-Facturas (<c>Item_Det_Fact</c>).</summary>
    private static readonly HashSet<IndicadorFacturacion> IndicadoresTicketFactura =
    [
        IndicadorFacturacion.Exento, IndicadorFacturacion.TasaMinima, IndicadorFacturacion.TasaBasica, IndicadorFacturacion.OtraTasa,
        IndicadorFacturacion.EntregaGratuita, IndicadorFacturacion.NoFacturable, IndicadorFacturacion.NoFacturableNegativo,
        IndicadorFacturacion.ExportacionYAsimiladas, IndicadorFacturacion.ImpuestoPercibido, IndicadorFacturacion.IvaEnSuspenso,
        IndicadorFacturacion.IvaMinimoMonotributo,
    ];

    /// <summary>Indicadores de la e-Boleta de entrada (<c>Item_Det_Boleta</c>).</summary>
    private static readonly HashSet<IndicadorFacturacion> IndicadoresBoleta =
    [
        IndicadorFacturacion.EntregaGratuita, IndicadorFacturacion.NoFacturable, IndicadorFacturacion.NoFacturableNegativo,
        IndicadorFacturacion.IvaEnSuspenso, IndicadorFacturacion.VendedorNoContribuyente, IndicadorFacturacion.VendedorIvaMinimo,
        IndicadorFacturacion.VendedorImeba, IndicadorFacturacion.CompraMonedaExtranjera,
    ];

    /// <summary>Indicadores que son solo de la e-Boleta de entrada.</summary>
    private static readonly HashSet<IndicadorFacturacion> IndicadoresSoloBoleta =
    [
        IndicadorFacturacion.VendedorNoContribuyente, IndicadorFacturacion.VendedorIvaMinimo,
        IndicadorFacturacion.VendedorImeba, IndicadorFacturacion.CompraMonedaExtranjera,
    ];

    /// <summary>
    /// Si una línea del tipo puede llevar el indicador. En remitos y resguardos el indicador solo se informa en los
    /// ítems a rebajar o anular (y en los de valor cero del e-Remito de exportación): el resto se ignora.
    /// </summary>
    private static bool IndicadorAdmitido(FamiliaCfe familia, IndicadorFacturacion indicador) => familia switch
    {
        FamiliaCfe.Ticket or FamiliaCfe.Factura => IndicadoresTicketFactura.Contains(indicador),
        FamiliaCfe.Boleta => IndicadoresBoleta.Contains(indicador),
        FamiliaCfe.FacturaExportacion => !IndicadoresSoloBoleta.Contains(indicador)
            && indicador is not (IndicadorFacturacion.RebajaRemito or IndicadorFacturacion.AnulacionResguardo),
        FamiliaCfe.RemitoExportacion => !IndicadoresSoloBoleta.Contains(indicador) && indicador is not (IndicadorFacturacion.AnulacionResguardo
            or IndicadorFacturacion.NoFacturable or IndicadorFacturacion.NoFacturableNegativo),
        FamiliaCfe.Remito => !IndicadoresSoloBoleta.Contains(indicador) && indicador is not (IndicadorFacturacion.AnulacionResguardo
            or IndicadorFacturacion.EntregaGratuita or IndicadorFacturacion.NoFacturable or IndicadorFacturacion.NoFacturableNegativo),
        _ => !IndicadoresSoloBoleta.Contains(indicador) && indicador is not (IndicadorFacturacion.RebajaRemito
            or IndicadorFacturacion.EntregaGratuita or IndicadorFacturacion.NoFacturable or IndicadorFacturacion.NoFacturableNegativo),
    };

    /// <summary>Si un descuento o recargo global del tipo puede llevar el indicador (<c>IndFactDR</c>).</summary>
    private static bool IndicadorGlobalAdmitido(FamiliaCfe familia, IndicadorFacturacion indicador) =>
        indicador is not (IndicadorFacturacion.EntregaGratuita or IndicadorFacturacion.RebajaRemito or IndicadorFacturacion.AnulacionResguardo)
        && familia switch
        {
            FamiliaCfe.Ticket or FamiliaCfe.Factura => IndicadoresTicketFactura.Contains(indicador),
            FamiliaCfe.Boleta => IndicadoresBoleta.Contains(indicador),
            FamiliaCfe.FacturaExportacion => !IndicadoresSoloBoleta.Contains(indicador),
            _ => false,
        };

    /// <summary>
    /// Valida el contenido del comprobante (sin numeración ni emisor, que valida la emisión) con las
    /// restricciones del esquema y del formato de la DGI: lo que pasa esta validación produce un XML válido. Las
    /// reglas que dependen del CAE (CAE especial) las valida <see cref="ValidarConCae"/> al emitir.
    /// </summary>
    /// <param name="hoy">Fecha actual en Uruguay (la de emisión si <see cref="FechaEmision"/> se omite).</param>
    /// <param name="topeMontoNetoSinReceptor">
    /// Tope en pesos (5.000 UI) a partir del cual un e-Ticket debe identificar al receptor; también el de la
    /// e-Boleta de compra de moneda extranjera sin vendedor identificado.
    /// </param>
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

        ValidarFechas(v, familia, fecha, hoy);
        v.Texto(nameof(NroInterno), NroInterno, 50);
        v.Texto(nameof(InfoAdicional), InfoAdicional, 150);
        if (Adenda is not null && !Validacion.EsTextoXml(Adenda))
            v.Error(nameof(Adenda), "Contiene caracteres de control no admitidos.");

        var maxLineas = familia == FamiliaCfe.Ticket ? 700 : 200;
        if (Detalle.Count == 0)
            v.Error(nameof(Detalle), "Debe tener al menos una línea.");
        else if (Detalle.Count > maxLineas)
            v.Error(nameof(Detalle), $"Admite hasta {maxLineas} líneas en este tipo de comprobante.");

        if (familia == FamiliaCfe.Resguardo)
            ValidarResguardo(v);
        else
            ValidarLineas(v, familia);
        ValidarCodigosRetencion(v, familia);

        if (!Moneda.EsValida)
            v.Error(nameof(Moneda), "No es una moneda de la tabla de la DGI (código ISO 4217, p. ej. \"USD\").");
        if (!Enum.IsDefined(FormaPago))
            v.Error(nameof(FormaPago), "No es válida.");
        if (Moneda != Moneda.PesoUruguayo)
        {
            if (TipoCambio is { } tipoCambio)
                v.TipoCambio(nameof(TipoCambio), tipoCambio);
            else
                v.Error(nameof(TipoCambio), "Es obligatorio cuando la moneda no es el peso uruguayo.");
        }

        ValidarIndicadoresDeOperacion(v, familia);
        ValidarReferencias(v, familia, hoy);
        ValidarExportacion(v, familia);
        ValidarReceptor(v, familia);

        if (Tipo.EsRemito())
        {
            if (IndTraslado is null)
                v.Error(nameof(IndTraslado), "Es obligatorio en los e-Remitos (motivo del traslado).");
            else if (!Enum.IsDefined(IndTraslado.Value))
                v.Error(nameof(IndTraslado), "No es un motivo de traslado válido.");
            if (Propietario is { } p)
            {
                v.Documento(nameof(Propietario), p.TipoDocumento, p.CodigoPais, p.Documento);
                v.Texto("Propietario.Nombre", p.Nombre, 150, obligatorio: true);
            }
        }
        else if (Propietario is not null)
            v.Error(nameof(Propietario), "Solo se informa en los e-Remitos (bienes de terceros).");

        ValidarMandante(v, familia);
        ValidarDescuentosYMedios(v, familia);
        ValidarSubtotales(v);

        if (IvaOtraTasa is { } ivaOtraTasa)
        {
            if (familia is not (FamiliaCfe.Ticket or FamiliaCfe.Factura))
                v.Error(nameof(IvaOtraTasa), "Solo corresponde a e-Tickets y e-Facturas.");
            else
                v.Monto(nameof(IvaOtraTasa), ivaOtraTasa);
        }

        // Con el indicador 16 el CFE informa MntBruto 3, que no admite líneas exentas ni gravadas.
        if (UsaIvaMinimoMonotributo)
            ValidarIndicadores(v, IndicadoresConIva,
                "No se admite junto con líneas de IVA mínimo o Monotributo (IvaMinimoMonotributo): esos CFE no discriminan IVA.");

        // Los totales se calculan con el resto válido: con montos fuera de rango podrían desbordar.
        if (v.Errores.Count == 0)
            ValidarTotales(v, familia, topeMontoNetoSinReceptor);

        return v.Errores;
    }

    /// <summary>
    /// Valida las reglas que dependen del CAE con que se numera: un CAE exonerado no admite líneas gravadas,
    /// uno de IVA mínimo o Monotributo solo admite sus indicadores (y el CFE informa <c>MntBruto</c> 3), y el
    /// indicador 16 exige uno de estos o una venta por cuenta ajena.
    /// </summary>
    internal List<ErrorValidacion> ValidarConCae(Cae cae)
    {
        var v = new Validacion();
        if (cae.Especial == CaeEspecial.Exonerado)
            ValidarIndicadores(v,
                [IndicadorFacturacion.TasaMinima, IndicadorFacturacion.TasaBasica, IndicadorFacturacion.OtraTasa,
                 IndicadorFacturacion.ImpuestoPercibido, IndicadorFacturacion.IvaEnSuspenso],
                "El CAE exonerado no admite líneas gravadas, con impuesto percibido ni con IVA en suspenso.");
        else if (cae.EsIvaMinimoOMonotributo)
        {
            ValidarIndicadores(v, IndicadoresConIva,
                "Con un CAE de IVA mínimo o Monotributo las líneas van con el indicador IvaMinimoMonotributo (16).");
            if (cae.Especial is CaeEspecial.Monotributo or CaeEspecial.MonotributoSocialMides && Emisor is { IvaAlDia: false })
                v.Error("Emisor.IvaAlDia", "La constancia de IVA al día no corresponde a los contribuyentes Monotributo.");
            if (MontosBrutos is not null)
                v.Error(nameof(MontosBrutos), "Con un CAE de IVA mínimo o Monotributo el CFE informa montos de IVA mínimo o Monotributo (MntBruto 3).");
        }
        else if (UsaIvaMinimoMonotributo && !Tipo.EsCuentaAjena())
            ValidarIndicadores(v, [IndicadorFacturacion.IvaMinimoMonotributo],
                "Es solo para contribuyentes IVA mínimo o Monotributo (con su CAE especial) o ventas por cuenta ajena de un mandante IVA mínimo.");
        return v.Errores;
    }

    /// <summary>Un error por cada línea o descuento/recargo cuyo indicador efectivo está en <paramref name="prohibidos"/>.</summary>
    private void ValidarIndicadores(Validacion v, IReadOnlyCollection<IndicadorFacturacion> prohibidos, string mensaje)
    {
        if (Tipo.EsRemito() || Tipo.Familia() == FamiliaCfe.Resguardo)
            return;
        for (int i = 0; i < Detalle.Count; i++)
            if (prohibidos.Contains(IndicadorInformado(Detalle[i].IndFact)))
                v.Error($"{nameof(Detalle)}[{i}].IndFact", mensaje);
        for (int i = 0; i < DescuentosRecargos.Count; i++)
            if (prohibidos.Contains(IndicadorInformado(DescuentosRecargos[i].IndFact)))
                v.Error($"{nameof(DescuentosRecargos)}[{i}].IndFact", mensaje);
    }

    private void ValidarFechas(Validacion v, FamiliaCfe familia, DateOnly fecha, DateOnly hoy)
    {
        if (fecha > hoy.AddDays(DiasFechaPosterior))
            v.Error(nameof(FechaEmision), $"Puede ser posterior a la fecha actual hasta {DiasFechaPosterior} días.");
        else
            v.Fecha(nameof(FechaEmision), fecha);

        if (FechaVencimiento is { } venc)
        {
            if (familia is FamiliaCfe.Remito or FamiliaCfe.RemitoExportacion or FamiliaCfe.Resguardo)
                v.Error(nameof(FechaVencimiento), "No corresponde a remitos ni resguardos.");
            else if (venc < fecha)
                v.Error(nameof(FechaVencimiento), "No puede ser anterior a la fecha de emisión.");
            else if (venc > Validacion.FechaMaxima)
                v.Error(nameof(FechaVencimiento), "Debe ser hasta el 31/12/2050.");
        }

        if (PeriodoDesde is not null || PeriodoHasta is not null)
        {
            if (familia is FamiliaCfe.Remito or FamiliaCfe.RemitoExportacion or FamiliaCfe.Resguardo)
                v.Error(nameof(PeriodoDesde), "El período facturado no corresponde a remitos ni resguardos.");
            if (PeriodoDesde is { } desde)
                v.Fecha(nameof(PeriodoDesde), desde);
            if (PeriodoHasta is { } hasta)
            {
                if (PeriodoDesde is { } inicio && hasta < inicio)
                    v.Error(nameof(PeriodoHasta), "No puede ser anterior a PeriodoDesde.");
                else
                    v.Fecha(nameof(PeriodoHasta), hasta);
            }
        }

        if (FechaValor is { } valor)
        {
            if (familia != FamiliaCfe.Resguardo)
                v.Error(nameof(FechaValor), "Solo corresponde al e-Resguardo (de organismos del Estado).");
            else if (valor.Year * 12 + valor.Month > fecha.Year * 12 + fecha.Month)
                v.Error(nameof(FechaValor), "Su mes no puede ser posterior al de la fecha de emisión.");
            else
                v.Fecha(nameof(FechaValor), valor);
        }
    }

    /// <summary>Indicadores de la operación: secreto profesional, pagos por cuenta de terceros, cobranza propia, compra de moneda extranjera y montos brutos.</summary>
    private void ValidarIndicadoresDeOperacion(Validacion v, FamiliaCfe familia)
    {
        if (PagoCuentaTerceros && familia is not (FamiliaCfe.Ticket or FamiliaCfe.Factura or FamiliaCfe.FacturaExportacion))
            v.Error(nameof(PagoCuentaTerceros), "Solo corresponde a e-Tickets, e-Facturas y e-Facturas de exportación.");

        if (CobranzaPropia)
        {
            if (familia is not (FamiliaCfe.Ticket or FamiliaCfe.Factura) || Tipo.EsNotaCorreccion())
                v.Error(nameof(CobranzaPropia), "Solo corresponde a e-Tickets y e-Facturas (no a sus notas).");
            else
            {
                for (int i = 0; i < Detalle.Count; i++)
                    if (Detalle[i].IndFact is not (IndicadorFacturacion.NoFacturable or IndicadorFacturacion.NoFacturableNegativo))
                        v.Error($"{nameof(Detalle)}[{i}].IndFact", "En una cobranza propia las líneas son no facturables (NoFacturable o NoFacturableNegativo).");
                if (DescuentosRecargos.Count > 0)
                    v.Error(nameof(DescuentosRecargos), "Una cobranza propia no admite descuentos ni recargos globales.");
                if (Referencias.Count == 0)
                    v.Error(nameof(Referencias), "Una cobranza propia debe referenciar los comprobantes que se cobran.");
            }
        }

        if (CompraMonedaExtranjera is { } compra)
        {
            if (familia != FamiliaCfe.Boleta)
                v.Error(nameof(CompraMonedaExtranjera), "Solo corresponde a la e-Boleta de entrada.");
            else if (!Enum.IsDefined(compra))
                v.Error(nameof(CompraMonedaExtranjera), "No es válido.");
            else
            {
                if (compra == UruFactura.CompraMonedaExtranjera.Reventa && Moneda != Moneda.PesoUruguayo)
                    v.Error(nameof(Moneda), "La compra de moneda extranjera para su reventa se documenta en pesos uruguayos.");
                if (compra == UruFactura.CompraMonedaExtranjera.ReventaPorCuentaAjena && SecretoProfesional)
                    v.Error(nameof(SecretoProfesional), "No corresponde en la compra de moneda extranjera por cuenta ajena.");
                for (int i = 0; i < Detalle.Count; i++)
                    if (Detalle[i].IndFact != IndicadorFacturacion.CompraMonedaExtranjera)
                        v.Error($"{nameof(Detalle)}[{i}].IndFact", "En la compra de moneda extranjera las líneas van con el indicador CompraMonedaExtranjera (17).");
                for (int i = 0; i < DescuentosRecargos.Count; i++)
                    if (DescuentosRecargos[i].IndFact != IndicadorFacturacion.CompraMonedaExtranjera)
                        v.Error($"{nameof(DescuentosRecargos)}[{i}].IndFact", "En la compra de moneda extranjera los descuentos y recargos van con el indicador CompraMonedaExtranjera (17).");
            }
        }
        else if (familia == FamiliaCfe.Boleta)
        {
            for (int i = 0; i < Detalle.Count; i++)
                if (Detalle[i].IndFact == IndicadorFacturacion.CompraMonedaExtranjera)
                    v.Error($"{nameof(Detalle)}[{i}].IndFact", "El indicador CompraMonedaExtranjera (17) exige CompraMonedaExtranjera.");
            for (int i = 0; i < DescuentosRecargos.Count; i++)
                if (DescuentosRecargos[i].IndFact == IndicadorFacturacion.CompraMonedaExtranjera)
                    v.Error($"{nameof(DescuentosRecargos)}[{i}].IndFact", "El indicador CompraMonedaExtranjera (17) exige CompraMonedaExtranjera.");
        }

        if (MontosBrutos is { } brutos)
        {
            if (!Enum.IsDefined(brutos))
                v.Error(nameof(MontosBrutos), "No es válido.");
            else if (brutos == UruFactura.MontosBrutos.IvaIncluido)
            {
                if (familia is not (FamiliaCfe.Ticket or FamiliaCfe.Factura))
                    v.Error(nameof(MontosBrutos), "Los montos con IVA incluido solo corresponden a e-Tickets y e-Facturas.");
                else if (UsaIvaMinimoMonotributo)
                    v.Error(nameof(MontosBrutos), "Las líneas de IVA mínimo o Monotributo (16) no discriminan IVA: no se expresan con IVA incluido.");
                else if (Detalle.Any(l => l.IndFact == IndicadorFacturacion.OtraTasa) || DescuentosRecargos.Any(d => d.IndFact == IndicadorFacturacion.OtraTasa))
                    v.Error(nameof(MontosBrutos), "Con IVA incluido solo se desglosan las tasas mínima y básica: no se admiten líneas a otra tasa (OtraTasa).");
            }
            else if (familia != FamiliaCfe.Boleta)
                v.Error(nameof(MontosBrutos), "Los montos con IMEBA y adicionales incluidos solo corresponden a la e-Boleta de entrada.");
        }

        // A-C115: una e-Boleta con IVA en suspenso expresa sus montos con IMEBA y adicionales incluidos.
        if (familia == FamiliaCfe.Boleta && MontosBrutos != UruFactura.MontosBrutos.ImebaYAdicionalesIncluidos
            && (Detalle.Any(l => l.IndFact == IndicadorFacturacion.IvaEnSuspenso) || DescuentosRecargos.Any(d => d.IndFact == IndicadorFacturacion.IvaEnSuspenso)))
            v.Error(nameof(MontosBrutos), "La e-Boleta de entrada con IVA en suspenso expresa sus montos con IMEBA y adicionales incluidos (ImebaYAdicionalesIncluidos).");
    }

    private void ValidarLineas(Validacion v, FamiliaCfe familia)
    {
        var conPrecio = familia != FamiliaCfe.Remito;
        // Item_Rem e Item_Rem_Exp no informan descuentos ni recargos; solo e-Tickets, e-Facturas y e-Boletas, retenciones.
        var conAjustes = familia is FamiliaCfe.Ticket or FamiliaCfe.Factura or FamiliaCfe.FacturaExportacion or FamiliaCfe.Boleta;
        var conRetenciones = familia is FamiliaCfe.Ticket or FamiliaCfe.Factura or FamiliaCfe.Boleta;
        var rutVendedor = familia == FamiliaCfe.Boleta && Receptor is { TipoDocumento: TipoDocumentoReceptor.Rut } r && !string.IsNullOrWhiteSpace(r.Documento);

        for (int i = 0; i < Detalle.Count; i++)
        {
            var linea = Detalle[i];
            var c = $"{nameof(Detalle)}[{i}]";
            v.Texto($"{c}.NombreItem", linea.NombreItem, 80, obligatorio: true);
            v.Texto($"{c}.Descripcion", linea.Descripcion, 1000);
            v.Texto($"{c}.UnidadMedida", linea.UnidadMedida, 4);
            if (linea.Ncm is not null && !Tipo.EsExportacion())
                v.Error($"{c}.Ncm", "Solo se informa en la e-Factura y el e-Remito de exportación.");
            else
                v.Texto($"{c}.Ncm", linea.Ncm, 20);

            var indicador = linea.IndFact;
            if (!Enum.IsDefined(indicador) || !IndicadorAdmitido(familia, indicador))
                v.Error($"{c}.IndFact", MensajeIndicador(familia));
            else if (familia == FamiliaCfe.Boleta)
            {
                // El indicador dice quién es el vendedor: sin RUT (13) o con RUT (14 y 15).
                if (indicador == IndicadorFacturacion.VendedorNoContribuyente && rutVendedor)
                    v.Error($"{c}.IndFact", "El vendedor no contribuyente (13) no se identifica con RUT.");
                else if (indicador is IndicadorFacturacion.VendedorIvaMinimo or IndicadorFacturacion.VendedorImeba && !rutVendedor)
                    v.Error($"{c}.IndFact", "El vendedor IVA mínimo, Monotributo o IMEBA (14 y 15) se identifica con su RUT (Receptor).");
            }
            var informado = IndicadorInformado(indicador);
            var gratuita = informado == IndicadorFacturacion.EntregaGratuita;
            var rebaja = indicador == IndicadorFacturacion.RebajaRemito;

            var errores = v.Errores.Count;
            var cantidad = CfeFormat.Redondear(linea.Cantidad, 3);
            if (cantidad == 0)
                v.Error($"{c}.Cantidad", "No puede ser cero (se informa con 3 decimales).");
            else if (Math.Abs(linea.Cantidad) > Validacion.CantidadMaxima)
                v.Error($"{c}.Cantidad", "Supera el máximo del formato CFE (14 dígitos enteros).");
            if (conPrecio)
            {
                if (linea.PrecioUnitario < 0)
                    v.Error($"{c}.PrecioUnitario", "No puede ser negativo.");
                else if (linea.PrecioUnitario > Validacion.PrecioMaximo)
                    v.Error($"{c}.PrecioUnitario", "Supera el máximo del formato CFE (11 dígitos enteros).");
                else if (gratuita && linea.PrecioUnitario != 0)
                    v.Error($"{c}.PrecioUnitario", "Las entregas gratuitas (y los ítems de valor cero) van con precio cero.");
                else if (!gratuita && !rebaja && CfeFormat.Redondear(linea.PrecioUnitario, 6) == 0)
                    v.Error($"{c}.PrecioUnitario", "Debe ser mayor a cero (salvo en las entregas gratuitas y en los ítems a rebajar de un remito).");
            }

            var conDescuentoORecargo = linea.DescuentoMonto != 0 || linea.DescuentoPorcentaje is not (null or 0)
                || linea.RecargoMonto != 0 || linea.RecargoPorcentaje is not (null or 0)
                || linea.Subdescuentos.Count > 0 || linea.Subrecargos.Count > 0;
            if (!conAjustes)
            {
                if (conDescuentoORecargo)
                    v.Error(c, "Los remitos no admiten descuentos ni recargos por línea.");
            }
            else
            {
                v.Monto($"{c}.DescuentoMonto", linea.DescuentoMonto);
                v.Monto($"{c}.RecargoMonto", linea.RecargoMonto);
                if (linea.DescuentoPorcentaje is < 0 or > 100)
                    v.Error($"{c}.DescuentoPorcentaje", "Debe estar entre 0 y 100.");
                if (linea.RecargoPorcentaje is < 0 or > 100)
                    v.Error($"{c}.RecargoPorcentaje", "Debe estar entre 0 y 100.");
                if (gratuita && conDescuentoORecargo)
                    v.Error($"{c}.PrecioUnitario", "Las entregas gratuitas van con precio cero, sin descuentos ni recargos (el monto del ítem es cero).");
                else if (cantidad < 0 && conDescuentoORecargo)
                    v.Error($"{c}.Cantidad", "Una cantidad negativa elimina un ítem del propio comprobante: va sin descuentos ni recargos.");
                ValidarDistribucion(v, $"{c}.Subdescuentos", linea.Subdescuentos, linea.DescuentoEfectivo, "descuento");
                ValidarDistribucion(v, $"{c}.Subrecargos", linea.Subrecargos, linea.RecargoEfectivo, "recargo");
            }

            // Con cantidad, precio y ajustes en rango, el monto de la línea no puede desbordar.
            if (conPrecio && v.Errores.Count == errores && cantidad > 0)
            {
                if (linea.DescuentoEfectivo > linea.Bruto)
                    v.Error($"{c}.DescuentoMonto", "No puede superar Cantidad × PrecioUnitario.");
                else if (linea.MontoTotal > Validacion.MontoMaximo)
                    v.Error(c, "El monto de la línea supera el máximo del formato CFE (15 dígitos enteros).");
            }
            else if (conPrecio && v.Errores.Count == errores && -linea.MontoTotal > Validacion.MontoMaximo)
                v.Error(c, "El monto de la línea supera el máximo del formato CFE (15 dígitos enteros).");

            ValidarCodigos(v, c, linea.Codigos);

            if (conRetenciones)
            {
                if (linea.Retenciones.Count > 5)
                    v.Error($"{c}.Retenciones", "Admite hasta 5 retenciones/percepciones por línea.");
                for (int j = 0; j < linea.Retenciones.Count; j++)
                    ValidarRetencion(v, $"{c}.Retenciones[{j}]", linea.Retenciones[j], resguardo: false);
                // B-C6: el indicador de agente o responsable va si y solo si la línea documenta retenciones.
                if (linea.Retenciones.Count > 0 && linea.AgenteResponsable is null)
                    v.Error($"{c}.AgenteResponsable", "Es obligatorio en las líneas con retenciones o percepciones.");
                else if (linea.Retenciones.Count == 0 && linea.AgenteResponsable is not null)
                    v.Error($"{c}.AgenteResponsable", "Solo se informa en las líneas con retenciones o percepciones.");
                else if (linea.AgenteResponsable is { } agente && !Enum.IsDefined(agente))
                    v.Error($"{c}.AgenteResponsable", "No es válido.");
            }
            else
            {
                if (linea.Retenciones.Count > 0)
                    v.Error($"{c}.Retenciones", "No se informan en este tipo de comprobante (solo en e-Tickets, e-Facturas, e-Resguardos y e-Boletas).");
                if (linea.AgenteResponsable is not null)
                    v.Error($"{c}.AgenteResponsable", "No corresponde a este tipo de comprobante.");
            }
        }
    }

    private static string MensajeIndicador(FamiliaCfe familia) => familia switch
    {
        FamiliaCfe.Boleta => "No es un indicador de la e-Boleta de entrada: EntregaGratuita, NoFacturable, NoFacturableNegativo, IvaEnSuspenso, VendedorNoContribuyente, VendedorIvaMinimo, VendedorImeba o CompraMonedaExtranjera.",
        FamiliaCfe.Remito => "En el e-Remito el indicador solo se informa en los ítems a rebajar de otro remito (RebajaRemito).",
        FamiliaCfe.RemitoExportacion => "En el e-Remito de exportación el indicador solo se informa en los ítems de valor cero (EntregaGratuita) o a rebajar de otro remito (RebajaRemito).",
        FamiliaCfe.Resguardo => "En el e-Resguardo el indicador solo se informa en los ítems a anular de otro resguardo (AnulacionResguardo).",
        _ => "No es un indicador de facturación válido para este tipo de comprobante.",
    };

    /// <summary>Distribución de un descuento o recargo: hasta 5 partes, solo si la línea tiene ese descuento o recargo.</summary>
    private static void ValidarDistribucion(Validacion v, string campo, IReadOnlyList<DistribucionAjuste> partes, decimal efectivo, string concepto)
    {
        if (partes.Count == 0)
            return;
        if (partes.Count > 5)
            v.Error(campo, "Admite hasta 5 partes.");
        if (efectivo <= 0)
            v.Error(campo, $"Solo se informa si la línea tiene {concepto}.");
        for (int i = 0; i < partes.Count; i++)
        {
            if (!Enum.IsDefined(partes[i].Tipo))
                v.Error($"{campo}[{i}].Tipo", "No es válido.");
            v.Monto($"{campo}[{i}].Valor", partes[i].Valor, admiteNegativo: true);
        }
    }

    private static void ValidarCodigos(Validacion v, string c, IReadOnlyList<CodigoItem> codigos)
    {
        if (codigos.Count > 5)
            v.Error($"{c}.Codigos", "Admite hasta 5 códigos.");
        for (int j = 0; j < codigos.Count; j++)
        {
            var codigo = codigos[j];
            v.Texto($"{c}.Codigos[{j}].Tipo", codigo.Tipo, 10, obligatorio: true);
            v.Texto($"{c}.Codigos[{j}].Codigo", codigo.Codigo, 35, obligatorio: true);
            // B-C3: la DGI controla el dígito verificador de los códigos GTIN.
            if (codigo.Tipo is "GTIN8" or "GTIN12" or "GTIN13" or "GTIN14" && !Validacion.EsGtin(codigo.Tipo, codigo.Codigo ?? ""))
                v.Error($"{c}.Codigos[{j}].Codigo", $"No es un {codigo.Tipo} válido (dígitos y dígito verificador).");
        }
    }

    /// <summary>
    /// Una retención, percepción o crédito fiscal: código numérico de hasta 8 dígitos, valor y (en el e-Resguardo,
    /// obligatorio) monto sujeto mayores a cero.
    /// </summary>
    private static void ValidarRetencion(Validacion v, string c, RetencionPercepcion r, bool resguardo)
    {
        if (string.IsNullOrWhiteSpace(r.Codigo))
            v.Error($"{c}.Codigo", "Es obligatorio.");
        else if (r.Codigo.Length > 8 || !r.Codigo.All(char.IsAsciiDigit) || r.Codigo.All(d => d == '0'))
            v.Error($"{c}.Codigo", "Debe ser el código numérico de la DGI (formulario y línea, hasta 8 dígitos; p. ej. 2183211).");
        if (resguardo || r.MontoSujeto != 0)
        {
            if (CfeFormat.Redondear(r.MontoSujeto) <= 0)
                v.Error($"{c}.MontoSujeto", resguardo ? "Es obligatorio y mayor a cero en el e-Resguardo." : "Debe ser mayor a cero (o cero para omitirlo).");
            else
                v.Monto($"{c}.MontoSujeto", r.MontoSujeto);
        }
        if (CfeFormat.Redondear(r.Valor) <= 0)
            v.Error($"{c}.Valor", "Debe ser mayor a cero.");
        else
            v.Monto($"{c}.Valor", r.Valor);
        if (r.Tasa is < 0 or > 100)
            v.Error($"{c}.Tasa", "Debe estar entre 0 y 100.");
        v.Texto($"{c}.InfoAdicional", r.InfoAdicional, 150);
    }

    private void ValidarResguardo(Validacion v)
    {
        for (int i = 0; i < Detalle.Count; i++)
        {
            var linea = Detalle[i];
            var c = $"{nameof(Detalle)}[{i}]";
            if (!Enum.IsDefined(linea.IndFact) || !IndicadorAdmitido(FamiliaCfe.Resguardo, linea.IndFact))
                v.Error($"{c}.IndFact", MensajeIndicador(FamiliaCfe.Resguardo));
            if (linea.Retenciones.Count is < 1 or > 5)
                v.Error($"{c}.Retenciones", "El e-Resguardo requiere de 1 a 5 retenciones/percepciones por línea.");
            if (linea.AgenteResponsable is not null)
                v.Error($"{c}.AgenteResponsable", "No corresponde al e-Resguardo.");

            for (int j = 0; j < linea.Retenciones.Count; j++)
                ValidarRetencion(v, $"{c}.Retenciones[{j}]", linea.Retenciones[j], resguardo: true);
        }
    }

    /// <summary>Hasta 20 códigos distintos (los totales los acumulan por código); la e-Boleta no informa créditos fiscales.</summary>
    private void ValidarCodigosRetencion(Validacion v, FamiliaCfe familia)
    {
        var codigos = Detalle.SelectMany(l => l.Retenciones).Select(r => r.Codigo ?? "").Distinct(StringComparer.Ordinal).ToList();
        if (codigos.Count > 20)
            v.Error(nameof(Detalle), "Admite hasta 20 códigos de retención o percepción distintos en el comprobante.");
        if (familia == FamiliaCfe.Boleta && codigos.Any(EsCreditoFiscal))
            v.Error(nameof(Detalle), "La e-Boleta de entrada no informa créditos fiscales (códigos 2181xxx).");
    }

    private void ValidarReferencias(Validacion v, FamiliaCfe familia, DateOnly hoy)
    {
        var nota = Tipo.EsNotaCorreccion();
        if (nota && Referencias.Count == 0)
            v.Error(nameof(Referencias), "Las notas de crédito y débito deben referenciar al menos un CFE.");
        if (Referencias.Count > 40)
            v.Error(nameof(Referencias), "Admite hasta 40 referencias.");
        // Los ítems a rebajar o anular indican, en la referencia, el remito o resguardo que ajustan.
        if (Referencias.Count == 0 && Detalle.Any(l => familia is FamiliaCfe.Remito or FamiliaCfe.RemitoExportacion && l.IndFact == IndicadorFacturacion.RebajaRemito
                || familia == FamiliaCfe.Resguardo && l.IndFact == IndicadorFacturacion.AnulacionResguardo))
            v.Error(nameof(Referencias), familia == FamiliaCfe.Resguardo
                ? "Los ítems a anular (AnulacionResguardo) deben referenciar el e-Resguardo que anulan."
                : "Los ítems a rebajar (RebajaRemito) deben referenciar el e-Remito que ajustan.");

        var referenciados = new HashSet<(TipoCfe, string, long)>();
        for (int i = 0; i < Referencias.Count; i++)
        {
            var r = Referencias[i];
            var c = $"{nameof(Referencias)}[{i}]";

            if (r.Global)
            {
                // IndGlobal: el comprobante no se identifica, el motivo va en RazonRef (F-C2 a F-C10).
                v.Texto($"{c}.Razon", r.Razon, 90, obligatorio: true);
                if (r.TipoCfe is not null || r.Serie is not null || r.NroCfe is not null || r.FechaCfe is not null
                    || r.MontoCfeRef is not null || r.MonedaCfeRef is not null || r.TipoCambioCfeRef is not null)
                    v.Error(c, "La referencia global no identifica un comprobante: no admite tipo, serie, número, fecha, monto, moneda ni tipo de cambio.");
                continue;
            }

            if (r.TipoCfe is not { } tipo)
                v.Error($"{c}.TipoCfe", "Es obligatorio (salvo en la referencia global).");
            else if (!Enum.IsDefined(tipo))
                v.Error($"{c}.TipoCfe", "No es un tipo de CFE soportado.");
            if (!Validacion.EsSerie(r.Serie))
                v.Error($"{c}.Serie", "Debe ser una letra, dos letras o un dígito 1-9 y una letra (ej: \"A\").");
            if (r.NroCfe is not (>= 1 and <= 9_999_999))
                v.Error($"{c}.NroCfe", "Debe estar entre 1 y 9999999.");
            else if (r.TipoCfe is { } t && r.Serie is { } serie && !referenciados.Add((t, serie, r.NroCfe.Value)))
                v.Error(c, "Ya hay otra referencia al mismo comprobante (tipo, serie y número).");
            if (r.FechaCfe is { } fechaCfe)
            {
                if (fechaCfe > hoy.AddDays(DiasFechaPosterior))
                    v.Error($"{c}.FechaCfe", $"Puede ser posterior a la fecha actual hasta {DiasFechaPosterior} días (debe ser la del comprobante referenciado).");
                else
                    v.Fecha($"{c}.FechaCfe", fechaCfe);
            }
            v.Texto($"{c}.Razon", r.Razon, 90);

            if (familia == FamiliaCfe.Remito)
            {
                // F-C8 a F-C10 no corresponden en el e-Remito.
                if (r.MontoCfeRef is not null || r.MonedaCfeRef is not null || r.TipoCambioCfeRef is not null)
                    v.Error(c, "El e-Remito no informa monto, moneda ni tipo de cambio del comprobante referenciado.");
                continue;
            }

            // Desde el formato CFE v25 las notas informan monto y moneda del CFE referenciado (F-C8 y F-C9), y el
            // tipo de cambio si la moneda no es el peso (F-C10).
            if (r.MontoCfeRef is { } monto)
                v.Monto($"{c}.MontoCfeRef", monto);
            else if (nota)
                v.Error($"{c}.MontoCfeRef", "Es obligatorio en las notas de crédito y débito (formato CFE v25).");
            if (r.MonedaCfeRef is { } moneda && !moneda.EsValida)
                v.Error($"{c}.MonedaCfeRef", "No es una moneda de la tabla de la DGI.");
            else if (r.MonedaCfeRef is null && nota)
                v.Error($"{c}.MonedaCfeRef", "Es obligatoria en las notas de crédito y débito (formato CFE v25).");
            if (r.TipoCambioCfeRef is { } tipoCambio)
                v.TipoCambio($"{c}.TipoCambioCfeRef", tipoCambio);
            else if (r.MonedaCfeRef is { } m && m != Moneda.PesoUruguayo)
                v.Error($"{c}.TipoCambioCfeRef", "Es obligatorio si el comprobante referenciado no es en pesos (el tipo de cambio de ese comprobante).");
        }
    }

    private void ValidarExportacion(Validacion v, FamiliaCfe familia)
    {
        var exportacion = Tipo.EsExportacion();
        var clausulaValida = ClausulaVenta is "N/A"
            || ClausulaVenta is { Length: >= 1 and <= 3 } && ClausulaVenta.All(char.IsAsciiLetter);
        if (!exportacion)
        {
            if (ClausulaVenta is not null)
                v.Error(nameof(ClausulaVenta), "Solo corresponde a exportación.");
        }
        else if (familia == FamiliaCfe.FacturaExportacion ? !clausulaValida : !string.IsNullOrWhiteSpace(ClausulaVenta) && !clausulaValida)
            v.Error(nameof(ClausulaVenta), "Es obligatoria en la e-Factura de exportación: de 1 a 3 letras (ej: \"FOB\") o \"N/A\".");

        if (exportacion && ViaTransporte is not (1 or 2 or 3 or 8 or 9))
            v.Error(nameof(ViaTransporte), "Es obligatoria en exportación: 1 Marítimo, 2 Aéreo, 3 Terrestre, 8 No aplica o 9 Otro.");
        else if (!exportacion && ViaTransporte is not null)
            v.Error(nameof(ViaTransporte), "Solo corresponde a exportación.");

        // A-C14: modalidades de exportación en 121-124; exportación de servicios (90) en e-Facturas y e-Tickets
        // propios, y Tax Free (80) en e-Tickets propios.
        int[] admitidas = ((int)Tipo % 100) switch
        {
            21 or 22 or 23 or 24 => [1, 2, 3, 4, 90, 91, 99],
            1 or 2 or 3 => [80, 90],
            11 or 12 or 13 => [90],
            _ => [],
        };
        var obligatoria = Tipo is TipoCfe.EFacturaExportacion or TipoCfe.EFacturaExportacionContingencia
            or TipoCfe.ERemitoExportacion or TipoCfe.ERemitoExportacionContingencia;
        if (ModalidadVenta is { } modalidad ? !admitidas.Contains(modalidad) : obligatoria)
            v.Error(nameof(ModalidadVenta), admitidas.Length == 0
                ? "No corresponde a este tipo de comprobante."
                : $"Debe ser {string.Join(", ", admitidas)}{(obligatoria ? " (obligatoria en la e-Factura y el e-Remito de exportación)" : "")}.");
    }

    /// <summary>
    /// Identificación del receptor según el tipo (A-C60 a A-C66.1): documento, nombre y domicilio. Con secreto
    /// profesional nada es obligatorio, pero si se omite el receptor su identificación va en la adenda. Los
    /// requisitos que dependen del monto (tope de UI) los valida <see cref="ValidarTotales"/>.
    /// </summary>
    private void ValidarReceptor(Validacion v, FamiliaCfe familia)
    {
        const string c = nameof(Receptor);
        var conRetenciones = Detalle.Any(l => l.Retenciones.Count > 0);
        // e-Boleta: la compra de moneda extranjera a un vendedor sin RUT solo lo identifica por encima del tope.
        var boletaSinTope = familia == FamiliaCfe.Boleta && CompraMonedaExtranjera is not null
            && Receptor is not { TipoDocumento: TipoDocumentoReceptor.Rut };
        var exigido = !SecretoProfesional && Tipo.RequiereReceptor() && !boletaSinTope;

        if (Receptor is not { } r)
        {
            if (SecretoProfesional)
            {
                if (string.IsNullOrWhiteSpace(Adenda))
                    v.Error(nameof(Adenda), "Con secreto profesional y sin receptor, la identificación del receptor va en la adenda.");
            }
            else if (exigido)
                v.Error(c, familia switch
                {
                    FamiliaCfe.Factura => "Las e-Facturas requieren los datos del receptor.",
                    FamiliaCfe.Resguardo => "El e-Resguardo requiere los datos del receptor (el retenido).",
                    FamiliaCfe.Boleta => "La e-Boleta de entrada requiere los datos del vendedor (Receptor).",
                    FamiliaCfe.Remito => "El e-Remito requiere los datos del receptor.",
                    _ => "La exportación requiere los datos del receptor.",
                });
            else if (familia == FamiliaCfe.Ticket && conRetenciones)
                v.Error(c, "Un e-Ticket que documenta retenciones o percepciones identifica al receptor.");
            return;
        }

        var conDocumento = !string.IsNullOrWhiteSpace(r.Documento);
        if (conDocumento)
        {
            if (familia == FamiliaCfe.Factura && r.TipoDocumento != TipoDocumentoReceptor.Rut)
                v.Error($"{c}.TipoDocumento", "El receptor de una e-Factura se identifica con un RUT uruguayo.");
            else
                v.Documento(c, r.TipoDocumento, r.CodigoPais, r.Documento);
        }
        else if (exigido && familia is FamiliaCfe.Factura or FamiliaCfe.Remito or FamiliaCfe.Resguardo or FamiliaCfe.Boleta)
            v.Error($"{c}.Documento", familia == FamiliaCfe.Factura ? "La e-Factura requiere el RUT del receptor." : "Es obligatorio en este tipo de comprobante.");
        else if (!SecretoProfesional && familia == FamiliaCfe.Ticket && conRetenciones)
            v.Error($"{c}.Documento", "Un e-Ticket que documenta retenciones o percepciones identifica al receptor.");

        var exportacion = Tipo.EsExportacion();
        var conDomicilio = exigido && familia is not (FamiliaCfe.Resguardo or FamiliaCfe.Ticket);
        v.Texto($"{c}.RazonSocial", r.RazonSocial, 150, obligatorio: exigido);
        v.Texto($"{c}.Direccion", r.Direccion, 70, obligatorio: conDomicilio);
        v.Texto($"{c}.Ciudad", r.Ciudad, 30, obligatorio: conDomicilio);
        v.Texto($"{c}.Departamento", r.Departamento, 30, obligatorio: exigido && exportacion);
        v.Texto($"{c}.Pais", r.Pais, 30, obligatorio: exigido && exportacion);
        if (r.CodigoPostal is not null && !(r.CodigoPostal.Length is >= 1 and <= 5 && r.CodigoPostal.All(char.IsAsciiDigit)))
            v.Error($"{c}.CodigoPostal", "Debe ser numérico, de hasta 5 dígitos.");
        v.Texto($"{c}.InfoAdicional", r.InfoAdicional, 150);
        v.Texto($"{c}.CompraId", r.CompraId, 50);
        v.Texto($"{c}.LugarDestinoEntrega", r.LugarDestinoEntrega, 100);
    }

    private void ValidarMandante(Validacion v, FamiliaCfe familia)
    {
        var porCuentaAjena = familia == FamiliaCfe.Boleta && CompraMonedaExtranjera == UruFactura.CompraMonedaExtranjera.ReventaPorCuentaAjena;
        if (!Tipo.EsCuentaAjena() && !porCuentaAjena)
        {
            if (Mandante is not null)
                v.Error(nameof(Mandante), "Solo se informa en las ventas por cuenta ajena y en la compra de moneda extranjera por cuenta ajena.");
            return;
        }

        if (Mandante is not { } m)
        {
            // Con secreto profesional (SecProf 2) la venta por cuenta ajena no está obligada a informar el mandante.
            if (!SecretoProfesional || porCuentaAjena)
                v.Error(nameof(Mandante), porCuentaAjena
                    ? "La compra de moneda extranjera por cuenta ajena requiere los datos del mandante."
                    : "Las ventas por cuenta ajena requieren los datos del mandante.");
            return;
        }

        v.Documento(nameof(Mandante), m.TipoDocumento, m.CodigoPais, m.Documento);
        v.Texto("Mandante.Nombre", m.Nombre, 150, obligatorio: true);
        if (porCuentaAjena && m.TipoDocumento is not (TipoDocumentoReceptor.Nie or TipoDocumentoReceptor.Rut or TipoDocumentoReceptor.Nife))
            v.Error("Mandante.TipoDocumento", "En la compra de moneda extranjera por cuenta ajena el mandante se identifica con NIE, RUT o NIFE.");
        // K-C4: el mandante no puede ser el propio emisor.
        if (m.TipoDocumento == TipoDocumentoReceptor.Rut && Emisor is { } emisor && m.Documento == emisor.Rut)
            v.Error("Mandante.Documento", "El mandante no puede ser el propio emisor.");
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
            if (d.Monto < 0)
                v.Error($"{c}.Monto", "No puede ser negativo.");
            else if (d.Monto > 0)
                v.Monto($"{c}.Monto", d.Monto);
            else if (d.Porcentaje is null)
                v.Error($"{c}.Monto", "Indique el monto o el porcentaje (Porcentaje).");
            else if (d.Porcentaje is not (> 0 and <= 100))
                v.Error($"{c}.Porcentaje", "Debe ser mayor a 0 y hasta 100.");
            v.Texto($"{c}.Glosa", d.Glosa, 100, obligatorio: true);
            if (d.Codigo is < 0 or > 999)
                v.Error($"{c}.Codigo", "Debe tener hasta 3 dígitos.");
            if (!Enum.IsDefined(d.Tipo))
                v.Error($"{c}.Tipo", "No es válido.");
            if (!sinZona && (!Enum.IsDefined(d.IndFact) || !IndicadorGlobalAdmitido(familia, d.IndFact)))
                v.Error($"{c}.IndFact", "No es un indicador de facturación válido para un descuento o recargo de este tipo de comprobante.");
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

    private void ValidarSubtotales(Validacion v)
    {
        if (SubtotalesInformativos.Count > 20)
            v.Error(nameof(SubtotalesInformativos), "Admite hasta 20 subtotales informativos.");
        for (int i = 0; i < SubtotalesInformativos.Count; i++)
        {
            var s = SubtotalesInformativos[i];
            var c = $"{nameof(SubtotalesInformativos)}[{i}]";
            v.Texto($"{c}.Glosa", s.Glosa, 100, obligatorio: true);
            if (s.Orden is < 1 or > 99)
                v.Error($"{c}.Orden", "Debe estar entre 1 y 99.");
            v.Monto($"{c}.Valor", s.Valor, admiteNegativo: true);
        }
    }

    private void ValidarTotales(Validacion v, FamiliaCfe familia, decimal? topeMontoNetoSinReceptor)
    {
        var t = CalcularTotales();

        if (familia == FamiliaCfe.Resguardo)
        {
            if (Math.Abs(t.MontoRetenido) > Validacion.MontoMaximo || Math.Abs(t.MontoCreditoFiscal) > Validacion.MontoMaximo)
                v.Error(nameof(Detalle), "El total retenido supera el máximo del formato CFE (15 dígitos enteros).");
            return;
        }
        if (familia == FamiliaCfe.Remito)
            return;
        if (familia == FamiliaCfe.RemitoExportacion)
        {
            // Rebajando otros remitos, el total puede ser negativo.
            if (Math.Abs(t.MontoTotal) > Validacion.MontoMaximo)
                v.Error(nameof(Detalle), "El total del comprobante supera el máximo del formato CFE (15 dígitos enteros).");
            return;
        }

        // Un porcentaje sobre una base chica puede no llegar a 0,01.
        for (int i = 0; i < DescuentosRecargos.Count; i++)
        {
            if (MontoInformado(DescuentosRecargos[i]) <= 0)
                v.Error($"{nameof(DescuentosRecargos)}[{i}].Porcentaje",
                    "El porcentaje no llega a 0,01 sobre las líneas con su indicador de facturación (¿hay líneas con ese indicador?).");
        }

        // Las bases no pueden quedar negativas: un descuento global o líneas con cantidad negativa pueden dejarlas así.
        if (t.MontoTotal < 0 || t.MontoNoGravado < 0 || t.MontoExportacion < 0 || t.MontoImpuestoPercibido < 0
            || t.MontoNetoSuspendido < 0 || t.MontoNetoMinimo < 0 || t.MontoNetoBasico < 0 || t.MontoNetoOtraTasa < 0)
            v.Error(nameof(DescuentosRecargos), "Los descuentos globales (o las líneas con cantidad negativa) superan la base de su indicador de facturación.");
        else if (t.MontoTotal > Validacion.MontoMaximo)
            v.Error(nameof(Detalle), "El total del comprobante supera el máximo del formato CFE (15 dígitos enteros).");
        if (Math.Abs(t.MontoNoFacturable) > Validacion.MontoMaximo || Math.Abs(t.MontoPagar) > Validacion.MontoMaximo
            || Math.Abs(t.MontoRetenido) > Validacion.MontoMaximo || Math.Abs(t.MontoCreditoFiscal) > Validacion.MontoMaximo)
            v.Error(nameof(Detalle), "El monto no facturable, lo retenido o el total a pagar superan el máximo del formato CFE (15 dígitos enteros).");

        // MntNetoIVAOtra y MntIVAOtra van juntos (A-C118 y A-C123).
        if (t.MontoNetoOtraTasa > 0 && t.IvaOtraTasa <= 0)
            v.Error(nameof(IvaOtraTasa), "Es obligatorio (mayor a cero) si hay líneas gravadas a otra tasa.");
        else if (t.IvaOtraTasa > 0 && t.MontoNetoOtraTasa <= 0)
            v.Error(nameof(IvaOtraTasa), "Solo corresponde con líneas gravadas a otra tasa (IndFact OtraTasa).");

        if (topeMontoNetoSinReceptor is not { } tope || SecretoProfesional)
            return;
        var netoPesos = t.MontoNeto * (Moneda == Moneda.PesoUruguayo ? 1m : TipoCambio ?? 1m);
        if (familia == FamiliaCfe.Ticket && string.IsNullOrWhiteSpace(Receptor?.Documento) && netoPesos > tope)
            v.Error($"{nameof(Receptor)}.Documento",
                $"El e-Ticket supera el tope de monto neto sin identificar al receptor ({CfeFormat.MonetaryPdf(tope)} pesos): informe su documento.");
        // e-Boleta de compra de moneda extranjera a un vendedor sin RUT: lo identifica si no está por debajo del tope.
        if (familia == FamiliaCfe.Boleta && CompraMonedaExtranjera is not null && Receptor is not { TipoDocumento: TipoDocumentoReceptor.Rut }
            && netoPesos >= tope)
        {
            const string c = nameof(Receptor);
            const string mensaje = "La compra de moneda extranjera que no está por debajo del tope identifica al vendedor (documento, nombre y domicilio).";
            if (Receptor is not { } r)
                v.Error(c, mensaje);
            else if (string.IsNullOrWhiteSpace(r.Documento) || string.IsNullOrWhiteSpace(r.RazonSocial)
                     || string.IsNullOrWhiteSpace(r.Direccion) || string.IsNullOrWhiteSpace(r.Ciudad))
                v.Error(c, mensaje);
        }
    }
}
