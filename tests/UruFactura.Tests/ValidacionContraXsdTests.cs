using System.Text;

namespace UruFactura.Tests;

/// <summary>
/// La validación cubre el esquema: todo CFE que la pasa produce un XML válido contra los XSD de la DGI.
/// Si no fuera así, el CFE se emitiría (consumiendo su número) y la DGI lo rechazaría.
/// </summary>
public class ValidacionContraXsdTests
{
    [Fact]
    public void CfeAleatoriosQuePasanLaValidacion_GeneranXmlValidoContraElXsd()
    {
        var generador = new Generador(new Random(20261023));
        var validosPorTipo = new Dictionary<TipoCfe, int>();
        var indicadores = new HashSet<IndicadorFacturacion>();
        var especiales = new HashSet<CaeEspecial>();
        var usos = new Dictionary<string, int>();
        void Contar(string uso, bool usado)
        {
            if (usado)
                usos[uso] = usos.GetValueOrDefault(uso) + 1;
        }
        var fallas = new List<string>();
        var emitidos = new List<CfeEmitido>();

        for (var i = 0; i < 6000; i++)
        {
            var (cfe, cae) = generador.Cfe();
            if (TestData.Validar(cfe, generador.Tope).Count > 0 || cfe.ValidarConCae(cae).Count > 0)
                continue;

            validosPorTipo[cfe.Tipo] = validosPorTipo.GetValueOrDefault(cfe.Tipo) + 1;
            indicadores.UnionWith(cfe.Detalle.Select(cfe.IndicadorEscrito).OfType<IndicadorFacturacion>());
            if (cae.Especial is { } especial)
                especiales.Add(especial);
            Contar("referencia global", cfe.Referencias.Any(r => r.Global));
            Contar("descuento en porcentaje", cfe.DescuentosRecargos.Any(d => d.Monto == 0 && d.Porcentaje is not null));
            Contar("IVA incluido", cfe.MontosBrutos == MontosBrutos.IvaIncluido);
            Contar("IMEBA incluido", cfe.MontosBrutos == MontosBrutos.ImebaYAdicionalesIncluidos);
            Contar("retenciones en e-Ticket o e-Factura", cfe.Tipo.Familia() is FamiliaCfe.Ticket or FamiliaCfe.Factura && cfe.Detalle.Any(l => l.Retenciones.Count > 0));
            Contar("retenciones en e-Boleta", cfe.Tipo.Familia() == FamiliaCfe.Boleta && cfe.Detalle.Any(l => l.Retenciones.Count > 0));
            Contar("secreto profesional", cfe.SecretoProfesional);
            Contar("pago por cuenta de terceros", cfe.PagoCuentaTerceros);
            Contar("cobranza propia", cfe.CobranzaPropia);
            Contar("compra de moneda extranjera por cuenta ajena", cfe.CompraMonedaExtranjera == CompraMonedaExtranjera.ReventaPorCuentaAjena);
            Contar("bienes de terceros", cfe.Propietario is not null);
            Contar("fecha valor", cfe.FechaValor is not null);
            Contar("período", cfe.PeriodoDesde is not null);
            Contar("subtotales", cfe.SubtotalesInformativos.Count > 0);
            Contar("subdescuentos y subrecargos", cfe.Detalle.Any(l => l.Subdescuentos.Count > 0 || l.Subrecargos.Count > 0));
            Contar("recargo en porcentaje", cfe.Detalle.Any(l => l.RecargoMonto == 0 && l.RecargoPorcentaje > 0));
            Contar("cantidad negativa", cfe.Detalle.Any(l => l.Cantidad < 0));
            Contar("NCM", cfe.Detalle.Any(l => l.Ncm is not null));
            Contar("modalidad de venta en e-Ticket o e-Factura", cfe.ModalidadVenta is not null && !cfe.Tipo.EsExportacion());
            Contar("otra moneda", cfe.Moneda != Moneda.PesoUruguayo && cfe.Moneda != Moneda.DolarAmericano);
            Contar("NIE, C.I. o DNI", cfe.Receptor?.TipoDocumento is TipoDocumentoReceptor.Nie or TipoDocumentoReceptor.CedulaIdentidad or TipoDocumentoReceptor.Dni);

            var emitido = TestData.Emitido(cfe, numero: emitidos.Count % 100 + 1, cae: cae);
            emitidos.Add(emitido);
            var errores = TestData.ValidarContraXsd(TestData.Sobre(emitido));
            if (errores.Count > 0)
                fallas.Add($"{cfe.Tipo}: {string.Join(" | ", errores)}");
        }

        Assert.True(fallas.Count == 0, string.Join(Environment.NewLine, fallas.Take(10)));
        // El reporte diario con todos ellos (montos de cada familia, retenciones, pagos por cuenta de terceros,
        // rebajas y anulaciones) también es válido.
        var reporte = UruFactura.Xml.ReporteDiario.Generar(
            "219999820013", TestData.Dia, 1, 1, new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero), emitidos, generador.Tope);
        TestData.AssertValidoContraXsd(FirmaDigital.Firmar(reporte, TestData.Certificado, AlgoritmoFirma.Sha256).Xml);
        // El generador produce comprobantes válidos de todos los tipos, indicadores, CAE especiales y zonas
        // opcionales (si no, la prueba no prueba nada).
        Assert.Equal(Enum.GetValues<TipoCfe>().Order(), validosPorTipo.Keys.Order());
        Assert.Equal(Enum.GetValues<IndicadorFacturacion>().Order(), indicadores.Order());
        Assert.Equal(Enum.GetValues<CaeEspecial>().Order(), especiales.Order());
        string[] esperados =
        [
            "referencia global", "descuento en porcentaje", "IVA incluido", "IMEBA incluido", "retenciones en e-Ticket o e-Factura",
            "retenciones en e-Boleta", "secreto profesional", "pago por cuenta de terceros", "cobranza propia",
            "compra de moneda extranjera por cuenta ajena", "bienes de terceros", "fecha valor", "período", "subtotales",
            "subdescuentos y subrecargos", "recargo en porcentaje", "cantidad negativa", "NCM", "modalidad de venta en e-Ticket o e-Factura",
            "otra moneda", "NIE, C.I. o DNI",
        ];
        Assert.True(esperados.All(usos.ContainsKey), $"Sin casos válidos de: {string.Join(", ", esperados.Where(e => !usos.ContainsKey(e)))}");
    }

    /// <summary>CFE aleatorios: casi siempre con valores admitidos y, a veces, con alguno fuera del esquema.</summary>
    private sealed class Generador(Random rnd)
    {
        private static readonly DateOnly Hoy = TestData.Dia;

        /// <summary>Tope de 5.000 UI con que se valida (alto: casi ningún e-Ticket lo supera).</summary>
        public decimal Tope => 5_000m * 6.4m;

        /// <summary>Uno de cada 25 valores queda fuera de lo admitido.</summary>
        private bool Raro() => rnd.Next(25) == 0;

        private bool Uno(int de) => rnd.Next(de) == 0;

        private string Texto(int max, bool obligatorio = false)
        {
            const string caracteres = "abcñáéZ09-/.,&<>\"' ";
            var largo = Raro() ? max + 1 : rnd.Next(obligatorio ? 1 : 0, max + 1);
            var sb = new StringBuilder();
            for (var i = 0; i < largo; i++)
                sb.Append(caracteres[rnd.Next(caracteres.Length)]);
            if (Raro() && sb.Length > 0)
                sb[0] = '\u0001';
            if (Uno(10) && sb.Length > 1)
            {
                sb.Length--;
                sb.Append("🎂"); // un par sustituto: 2 caracteres en .NET, 1 en el XSD
            }
            var texto = sb.ToString();
            return obligatorio && string.IsNullOrWhiteSpace(texto) ? "x" : texto;
        }

        private decimal Monto(decimal max, int decimales = 2) =>
            Raro()
                ? Uno(2) ? -1m : max * 1_000_000_000m
                : Math.Round((decimal)rnd.NextDouble() * max, rnd.Next(0, decimales + 2));

        private T De<T>(params T[] valores) => valores[rnd.Next(valores.Length)];

        private string Digitos(int largo) => string.Concat(Enumerable.Range(0, largo).Select(_ => (char)('0' + rnd.Next(10))));

        /// <summary>Un RUT con dígito verificador válido (o, a veces, uno inválido).</summary>
        private string RutValido()
        {
            if (Raro())
                return "214844360019";
            int[] ponderadores = [4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
            while (true)
            {
                var cuerpo = "21" + Digitos(9);
                var dv = 11 - cuerpo.Select((d, i) => (d - '0') * ponderadores[i]).Sum() % 11;
                if (dv == 10)
                    continue;
                return cuerpo + (dv == 11 ? 0 : dv);
            }
        }

        /// <summary>Una C.I. con dígito verificador válido (o, a veces, uno inválido).</summary>
        private string CiValida()
        {
            int[] ponderadores = [2, 9, 8, 7, 6, 3, 4];
            var cuerpo = Digitos(7);
            var dv = (10 - cuerpo.Select((d, i) => (d - '0') * ponderadores[i]).Sum() % 10) % 10;
            return cuerpo + (Raro() ? (dv + 1) % 10 : dv);
        }

        /// <summary>Un GTIN-13 con dígito verificador válido.</summary>
        private string Gtin13()
        {
            var cuerpo = Digitos(12);
            var suma = cuerpo.Select((d, i) => (d - '0') * ((11 - i) % 2 == 0 ? 3 : 1)).Sum();
            return cuerpo + (10 - suma % 10) % 10;
        }

        /// <summary>Un documento de identidad de cualquier tipo, con el país que le corresponde.</summary>
        private (TipoDocumentoReceptor Tipo, string Pais, string Documento) Documento(bool conRut = true)
        {
            var tipo = De(conRut
                ? [TipoDocumentoReceptor.Rut, TipoDocumentoReceptor.Rut, TipoDocumentoReceptor.Nie, TipoDocumentoReceptor.CedulaIdentidad,
                   TipoDocumentoReceptor.Otro, TipoDocumentoReceptor.Pasaporte, TipoDocumentoReceptor.Dni, TipoDocumentoReceptor.Nife]
                : new[] { TipoDocumentoReceptor.Nie, TipoDocumentoReceptor.CedulaIdentidad, TipoDocumentoReceptor.Otro,
                    TipoDocumentoReceptor.Pasaporte, TipoDocumentoReceptor.Dni, TipoDocumentoReceptor.Nife });
            return tipo switch
            {
                TipoDocumentoReceptor.Rut => (tipo, "UY", RutValido()),
                TipoDocumentoReceptor.Nie => (tipo, Raro() ? "AR" : "UY", "9" + Digitos(7)),
                TipoDocumentoReceptor.CedulaIdentidad => (tipo, "UY", CiValida()),
                TipoDocumentoReceptor.Dni => (tipo, Raro() ? "UY" : De("AR", "BR", "CL", "PY"), Digitos(8)),
                _ => (tipo, Raro() ? "ZZ" : De("US", "AR", "ES", "99", "UY"), $"X{rnd.Next(1, 999_999)}"),
            };
        }

        /// <summary>Indicadores de las líneas y los descuentos: los comunes, o los de IVA mínimo/Monotributo.</summary>
        private IndicadorFacturacion Indicador(bool ivaMinimo, bool linea)
        {
            if (Raro())
                return (IndicadorFacturacion)De(9, 13, 99);
            if (ivaMinimo)
                return De(IndicadorFacturacion.IvaMinimoMonotributo, IndicadorFacturacion.IvaMinimoMonotributo, IndicadorFacturacion.NoFacturable,
                    linea ? IndicadorFacturacion.EntregaGratuita : IndicadorFacturacion.NoFacturable, IndicadorFacturacion.ExportacionYAsimiladas);
            return De(
                IndicadorFacturacion.Exento, IndicadorFacturacion.TasaMinima, IndicadorFacturacion.TasaBasica, IndicadorFacturacion.TasaBasica,
                IndicadorFacturacion.OtraTasa, IndicadorFacturacion.NoFacturable, IndicadorFacturacion.NoFacturableNegativo,
                IndicadorFacturacion.ExportacionYAsimiladas, IndicadorFacturacion.ImpuestoPercibido, IndicadorFacturacion.IvaEnSuspenso,
                linea ? IndicadorFacturacion.EntregaGratuita : IndicadorFacturacion.TasaBasica);
        }

        /// <summary>Quién vende en una e-Boleta de entrada.</summary>
        private enum Vendedor { NoContribuyente, IvaMinimo, Imeba, MonedaExtranjera }

        public (Cfe Cfe, Cae Cae) Cfe()
        {
            var tipo = De(Enum.GetValues<TipoCfe>());
            var familia = tipo.Familia();
            var exportacion = tipo.EsExportacion();
            var remito = tipo.EsRemito();
            var boleta = familia == FamiliaCfe.Boleta;
            var ticketOFactura = familia is FamiliaCfe.Ticket or FamiliaCfe.Factura;
            var conMontos = !remito && familia != FamiliaCfe.Resguardo;

            // Uno de cada 6 e-Tickets o e-Facturas es de un contribuyente IVA mínimo o Monotributo (o de un mandante IVA mínimo).
            var ivaMinimo = ticketOFactura && Uno(6);
            var cae = TestData.Cae(tipo) with
            {
                Especial = ivaMinimo && !tipo.EsCuentaAjena() ? De(CaeEspecial.IvaMinimo, CaeEspecial.Monotributo, CaeEspecial.MonotributoSocialMides)
                    : !ivaMinimo && conMontos && Uno(12) ? CaeEspecial.Exonerado : null,
            };
            if (cae.Especial == CaeEspecial.Exonerado)
                cae = cae with { CausalEspecial = rnd.Next(1, 999) };
            var exonerado = cae.Especial == CaeEspecial.Exonerado;

            var vendedor = De(Enum.GetValues<Vendedor>());
            var compra = boleta && vendedor == Vendedor.MonedaExtranjera
                ? Raro() ? (CompraMonedaExtranjera)7 : De(CompraMonedaExtranjera.Reventa, CompraMonedaExtranjera.ReventaPorCuentaAjena)
                : (CompraMonedaExtranjera?)null;
            var cobranza = ticketOFactura && !tipo.EsNotaCorreccion() && !ivaMinimo && Uno(10);
            var ivaIncluido = ticketOFactura && !ivaMinimo && !cobranza && Uno(6);
            var conSuspenso = boleta && compra is null && Uno(4);

            var detalle = Enumerable.Range(0, Raro() ? 0 : rnd.Next(1, 5))
                .Select(_ => Linea(tipo, ivaMinimo, exonerado, cobranza, ivaIncluido, boleta ? IndicadorBoleta(vendedor, conSuspenso) : null))
                .ToList();
            // Una línea con cantidad negativa elimina parte de otra del mismo comprobante.
            if (detalle.Count > 0 && familia is not FamiliaCfe.Resguardo && Uno(8)
                && detalle[0] is { Cantidad: > 0.002m } original && original.IndFact is not (IndicadorFacturacion.RebajaRemito or IndicadorFacturacion.EntregaGratuita))
                detalle.Add(original with
                {
                    Cantidad = -Math.Round(original.Cantidad / 2, 3), DescuentoMonto = 0, DescuentoPorcentaje = null, RecargoMonto = 0,
                    RecargoPorcentaje = null, Subdescuentos = [], Subrecargos = [], Retenciones = [], AgenteResponsable = null,
                });
            var otraTasa = detalle.Any(l => l.IndFact == IndicadorFacturacion.OtraTasa) && !exportacion;
            var conRetenciones = detalle.Any(l => l.Retenciones.Count > 0);
            var rebaja = detalle.Any(l => l.IndFact is IndicadorFacturacion.RebajaRemito or IndicadorFacturacion.AnulacionResguardo);
            var secreto = Uno(15);
            var dolares = !boleta && Uno(4);
            var moneda = compra == CompraMonedaExtranjera.Reventa ? Moneda.PesoUruguayo
                : dolares ? Moneda.DolarAmericano
                : Uno(10) ? De(Moneda.Euro, Moneda.RealBrasileno, Moneda.PesoArgentino, Moneda.UnidadIndexada, new Moneda(Raro() ? "XYZ" : "CLP"))
                : Moneda.PesoUruguayo;

            return (new Cfe(tipo)
            {
                IvaOtraTasa = otraTasa && !Raro() ? 0.01m + Monto(500m) : null,
                Adenda = secreto || Uno(5) ? Texto(300, obligatorio: secreto) : null,
                FechaEmision = Hoy.AddDays(Raro() ? 61 : -rnd.Next(0, 3)),
                NroInterno = Uno(4) ? Texto(50) : null,
                InfoAdicional = Uno(4) ? Texto(150) : null,
                FechaVencimiento = !remito && familia != FamiliaCfe.Resguardo && Uno(4) ? Hoy.AddDays(Raro() ? -5 : rnd.Next(0, 60)) : null,
                PeriodoDesde = conMontos && !Raro() && Uno(6) ? Hoy.AddDays(-30) : null,
                PeriodoHasta = conMontos && Uno(6) ? Hoy.AddDays(Raro() ? -60 : 0) : null,
                FechaValor = familia == FamiliaCfe.Resguardo && Uno(4) ? Hoy.AddDays(Raro() ? 40 : -rnd.Next(0, 60)) : null,
                SecretoProfesional = secreto,
                PagoCuentaTerceros = (familia is FamiliaCfe.Ticket or FamiliaCfe.Factura or FamiliaCfe.FacturaExportacion || Raro()) && Uno(8),
                CobranzaPropia = cobranza,
                CompraMonedaExtranjera = compra,
                MontosBrutos = ivaIncluido && !otraTasa && !detalle.Any(l => l.IndFact == IndicadorFacturacion.IvaMinimoMonotributo) ? MontosBrutos.IvaIncluido
                    : conSuspenso && !Raro() ? MontosBrutos.ImebaYAdicionalesIncluidos
                    : Raro() ? De(MontosBrutos.IvaIncluido, MontosBrutos.ImebaYAdicionalesIncluidos) : null,
                FormaPago = De(FormaPago.Contado, FormaPago.Credito),
                Moneda = moneda,
                TipoCambio = moneda != Moneda.PesoUruguayo && !Raro() ? 0.001m + Monto(100m, 3) : null,
                Receptor = Receptor(tipo, vendedor, compra, secreto, conRetenciones),
                Emisor = Uno(6) ? TestData.Emisor() with { IvaAlDia = false } : null,
                Mandante = tipo.EsCuentaAjena() && !(secreto && Uno(2)) || compra == CompraMonedaExtranjera.ReventaPorCuentaAjena || Raro()
                    ? Mandante(compra == CompraMonedaExtranjera.ReventaPorCuentaAjena)
                    : null,
                IndTraslado = remito ? (Raro() ? (IndTraslado)9 : (IndTraslado)rnd.Next(1, 7)) : null,
                Propietario = remito && Uno(4) || Raro() ? Propietario() : null,
                ClausulaVenta = exportacion ? (Raro() ? "FOB1" : De("FOB", "CIF", "N/A", "EXW")) : null,
                ModalidadVenta = exportacion ? (Raro() ? 80 : De(1, 2, 3, 4, 90, 91, 99))
                    : (int)tipo % 100 is 1 or 2 or 3 && Uno(6) ? De(80, 90)
                    : (int)tipo % 100 is 11 or 12 or 13 && Uno(6) ? 90
                    : Raro() ? 1 : null,
                ViaTransporte = exportacion ? (Raro() ? 4 : De(1, 2, 3, 8, 9)) : null,
                Detalle = detalle,
                SubtotalesInformativos = Uno(6)
                    ? [.. Enumerable.Range(0, rnd.Next(1, 3)).Select(_ => new SubtotalInformativo { Glosa = Texto(100, true), Valor = Monto(500m) * (Uno(3) ? -1 : 1), Orden = Uno(2) ? rnd.Next(0, Raro() ? 120 : 99) : null })]
                    : [],
                DescuentosRecargos = conMontos && !cobranza && detalle.Count > 0 && Uno(4)
                    ?
                    [
                        // Sobre el indicador de alguna línea, en monto o en porcentaje.
                        new DescuentoRecargoGlobal
                        {
                            Glosa = Texto(100, true), Tipo = De(TipoMovimientoGlobal.Descuento, TipoMovimientoGlobal.Recargo),
                            Monto = Uno(2) ? 0.01m + Monto(30m) : 0m, Porcentaje = Uno(2) ? 0.001m + Monto(99m, 3) : null,
                            IndFact = Raro() ? IndicadorFacturacion.EntregaGratuita : De(detalle.ToArray()).IndFact,
                            Codigo = Uno(2) ? rnd.Next(0, 999) : null,
                        },
                    ]
                    : [],
                MediosPago = conMontos && Uno(4)
                    ? [new MedioPago { Glosa = Texto(100, true), Valor = Monto(300m), Orden = Uno(2) ? rnd.Next(0, Raro() ? 120 : 99) : null }]
                    : [],
                Referencias = tipo.EsNotaCorreccion() || cobranza || rebaja || Uno(5) ? [Referencia(familia == FamiliaCfe.Remito)] : [],
            }, cae);
        }

        /// <summary>El indicador de las líneas de una e-Boleta según el vendedor.</summary>
        private IndicadorFacturacion IndicadorBoleta(Vendedor vendedor, bool conSuspenso)
        {
            if (Raro())
                return De(IndicadorFacturacion.TasaBasica, IndicadorFacturacion.IvaMinimoMonotributo);
            if (Uno(6))
                return De(IndicadorFacturacion.EntregaGratuita, IndicadorFacturacion.NoFacturable, IndicadorFacturacion.NoFacturableNegativo);
            if (conSuspenso && Uno(3))
                return IndicadorFacturacion.IvaEnSuspenso;
            return vendedor switch
            {
                Vendedor.NoContribuyente => IndicadorFacturacion.VendedorNoContribuyente,
                Vendedor.IvaMinimo => IndicadorFacturacion.VendedorIvaMinimo,
                Vendedor.Imeba => IndicadorFacturacion.VendedorImeba,
                _ => IndicadorFacturacion.CompraMonedaExtranjera,
            };
        }

        private Receptor? Receptor(TipoCfe tipo, Vendedor vendedor, CompraMonedaExtranjera? compra, bool secreto, bool conRetenciones)
        {
            var familia = tipo.Familia();
            var exportacion = tipo.EsExportacion();
            if (secreto && Uno(2) || familia == FamiliaCfe.Ticket && !conRetenciones && Uno(2) || compra is not null && Uno(3))
                return null;

            var (tipoDocumento, pais, documento) = familia switch
            {
                FamiliaCfe.Factura => (TipoDocumentoReceptor.Rut, "UY", RutValido()),
                FamiliaCfe.Boleta => vendedor is Vendedor.IvaMinimo or Vendedor.Imeba ? (TipoDocumentoReceptor.Rut, "UY", RutValido())
                    : Documento(conRut: vendedor == Vendedor.MonedaExtranjera),
                _ when exportacion => (TipoDocumentoReceptor.Pasaporte, Raro() ? "ZZ" : De("US", "AR", "BR", "ES", "CN"), $"X{rnd.Next(1, 999_999)}"),
                _ => Documento(),
            };
            var conDocumento = !exportacion || Uno(2);
            var completo = !secreto || Uno(2);
            var conDomicilio = completo && familia is not (FamiliaCfe.Ticket or FamiliaCfe.Resguardo) || Uno(2);
            return new Receptor
            {
                Documento = conDocumento && (familia != FamiliaCfe.Ticket || conRetenciones || Uno(2)) ? documento : null,
                TipoDocumento = tipoDocumento,
                CodigoPais = pais,
                RazonSocial = Texto(150, obligatorio: completo && familia != FamiliaCfe.Ticket),
                Direccion = conDomicilio ? Texto(70, obligatorio: true) : null,
                Ciudad = conDomicilio ? Texto(30, obligatorio: true) : null,
                Departamento = exportacion && completo || Uno(2) ? Texto(30, obligatorio: exportacion) : null,
                Pais = exportacion && completo || Uno(3) ? Texto(30, obligatorio: exportacion) : null,
                CodigoPostal = Uno(3) ? rnd.Next(0, Raro() ? 999_999 : 99_999).ToString() : null,
                InfoAdicional = Uno(3) ? Texto(150) : null,
                CompraId = Uno(3) ? Texto(50) : null,
                LugarDestinoEntrega = Uno(3) ? Texto(100) : null,
            };
        }

        private Mandante Mandante(bool compraPorCuentaAjena)
        {
            var (tipo, pais, documento) = compraPorCuentaAjena && !Raro()
                ? De((TipoDocumentoReceptor.Rut, "UY", RutValido()), (TipoDocumentoReceptor.Nie, "UY", "9" + Digitos(7)), (TipoDocumentoReceptor.Nife, "US", Digitos(9)))
                : Documento();
            return new Mandante { TipoDocumento = tipo, CodigoPais = pais, Documento = documento, Nombre = Texto(150, true), IvaAlDia = !Uno(4) };
        }

        private Propietario Propietario()
        {
            var (tipo, pais, documento) = Documento();
            return new Propietario { TipoDocumento = tipo, CodigoPais = pais, Documento = documento, Nombre = Texto(150, obligatorio: !Raro()) };
        }

        private RefCfe Referencia(bool eRemito)
        {
            if (Uno(6))
                return new RefCfe { Global = true, Razon = Texto(90, obligatorio: !Raro()), NroCfe = Raro() ? 1 : null };

            var dolares = Uno(4);
            return new RefCfe
            {
                TipoCfe = TipoCfe.ETicket, Serie = Raro() ? "a" : De("A", "AB", "1A"), NroCfe = Raro() ? 0 : rnd.Next(1, 9_999_999),
                FechaCfe = Uno(3) ? null : Hoy.AddDays(Raro() ? 61 : -rnd.Next(3, 300)), Razon = Uno(2) ? Texto(90) : null,
                MontoCfeRef = eRemito && !Raro() || Raro() ? null : Monto(1000m),
                MonedaCfeRef = eRemito && !Raro() || Raro() ? null : dolares ? Moneda.DolarAmericano : Moneda.PesoUruguayo,
                TipoCambioCfeRef = dolares && !Raro() && !eRemito ? 0.001m + Monto(50m, 3) : null,
            };
        }

        private LineaDetalle Linea(TipoCfe tipo, bool ivaMinimo, bool exonerado, bool cobranza, bool ivaIncluido, IndicadorFacturacion? indicadorBoleta)
        {
            var familia = tipo.Familia();
            var remito = tipo.EsRemito();
            var resguardo = familia == FamiliaCfe.Resguardo;
            var indicador = indicadorBoleta ?? Indicador(ivaMinimo, linea: true);
            if (exonerado && !Raro() && indicador is IndicadorFacturacion.TasaMinima or IndicadorFacturacion.TasaBasica or IndicadorFacturacion.OtraTasa
                    or IndicadorFacturacion.ImpuestoPercibido or IndicadorFacturacion.IvaEnSuspenso)
                indicador = IndicadorFacturacion.Exento;
            if (cobranza && !Raro())
                indicador = De(IndicadorFacturacion.NoFacturable, IndicadorFacturacion.NoFacturable, IndicadorFacturacion.NoFacturableNegativo);
            if (ivaIncluido && indicador == IndicadorFacturacion.OtraTasa)
                indicador = IndicadorFacturacion.TasaBasica;
            // Los remitos rebajan ítems de otro (8) y el e-Remito de exportación informa ítems de valor cero (5); el e-Resguardo anula ítems (9).
            if (remito)
                indicador = Uno(6) ? IndicadorFacturacion.RebajaRemito
                    : familia == FamiliaCfe.RemitoExportacion && Uno(6) ? IndicadorFacturacion.EntregaGratuita
                    : Raro() ? IndicadorFacturacion.AnulacionResguardo : IndicadorFacturacion.TasaBasica;
            if (resguardo)
                indicador = Uno(5) ? IndicadorFacturacion.AnulacionResguardo : Raro() ? IndicadorFacturacion.RebajaRemito : IndicadorFacturacion.TasaBasica;

            var gratuita = tipo.EsExportacion() || familia != FamiliaCfe.Remito
                ? indicador == IndicadorFacturacion.EntregaGratuita && !Raro()
                : false;
            var conAjustes = !remito && !resguardo && !gratuita;
            var retenciones = resguardo || familia is FamiliaCfe.Ticket or FamiliaCfe.Factura or FamiliaCfe.Boleta && Uno(8) || Raro();
            var descuento = conAjustes && Uno(5) ? Monto(20m) : 0;
            return new LineaDetalle
            {
                NombreItem = resguardo ? "" : Texto(80, true),
                Descripcion = Uno(4) ? Texto(300) : null,
                Ncm = tipo.EsExportacion() && Uno(3) || Raro() ? Texto(20, true) : null,
                UnidadMedida = Uno(3) ? Texto(4) : null,
                Cantidad = 0.001m + Monto(1000m, 3),
                PrecioUnitario = familia == FamiliaCfe.Remito || gratuita || indicador == IndicadorFacturacion.RebajaRemito && Uno(2) ? 0
                    : (Raro() ? 0 : 0.000001m) + Monto(100_000m, 6),
                IndFact = indicador,
                DescuentoMonto = descuento,
                DescuentoPorcentaje = conAjustes && Uno(6) ? Monto(100m, 3) : null,
                Subdescuentos = descuento > 0 && Uno(3) || Raro()
                    ? [new DistribucionAjuste { Tipo = De(TipoValor.Monto, TipoValor.Porcentaje), Valor = Monto(20m) }]
                    : [],
                RecargoMonto = conAjustes && Uno(6) ? Monto(100m) : 0,
                RecargoPorcentaje = conAjustes && Uno(6) ? Monto(100m, 3) : null,
                Subrecargos = conAjustes && Uno(10) ? [new DistribucionAjuste { Tipo = TipoValor.Monto, Valor = Monto(20m) }] : [],
                Codigos = Uno(4)
                    ? [Uno(2) ? new CodigoItem { Tipo = "GTIN13", Codigo = Raro() ? "1234567890123" : Gtin13() } : new CodigoItem { Tipo = Texto(10, true), Codigo = Texto(35, true) }]
                    : [],
                AgenteResponsable = retenciones && !resguardo && !Raro() || Raro() ? De(AgenteResponsable.Producto, AgenteResponsable.ConceptoAsociado) : null,
                Retenciones = retenciones
                    ? [.. Enumerable.Range(0, Raro() ? 0 : rnd.Next(1, 4)).Select(_ => new RetencionPercepcion
                    {
                        Codigo = Raro() ? Texto(8, true) : De("2183211", "2183212", "1146171", "2181410", "9999001"),
                        Tasa = Uno(2) ? Monto(100m, 3) : null,
                        MontoSujeto = resguardo || Uno(2) ? 0.01m + Monto(100_000m) : 0, Valor = 0.01m + Monto(10_000m),
                        InfoAdicional = Uno(3) ? Texto(150) : null,
                    })]
                    : [],
            };
        }
    }
}
