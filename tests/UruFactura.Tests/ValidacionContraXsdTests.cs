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
        var fallas = new List<string>();

        for (var i = 0; i < 2500; i++)
        {
            var cfe = generador.Cfe();
            if (TestData.Validar(cfe).Count > 0)
                continue;

            validosPorTipo[cfe.Tipo] = validosPorTipo.GetValueOrDefault(cfe.Tipo) + 1;
            var errores = TestData.ValidarContraXsd(TestData.Sobre(TestData.Emitido(cfe)));
            if (errores.Count > 0)
                fallas.Add($"{cfe.Tipo}: {string.Join(" | ", errores)}");
        }

        Assert.True(fallas.Count == 0, string.Join(Environment.NewLine, fallas.Take(10)));
        // El generador produce comprobantes válidos de todos los tipos (si no, la prueba no prueba nada).
        Assert.Equal(Enum.GetValues<TipoCfe>().Order(), validosPorTipo.Keys.Order());
    }

    /// <summary>CFE aleatorios: casi siempre con valores admitidos y, a veces, con alguno fuera del esquema.</summary>
    private sealed class Generador(Random rnd)
    {
        private static readonly DateOnly Hoy = TestData.Dia;

        /// <summary>Uno de cada 25 valores queda fuera de lo admitido.</summary>
        private bool Raro() => rnd.Next(25) == 0;

        private string Texto(int max, bool obligatorio = false)
        {
            const string caracteres = "abcñáéZ09-/.,&<>\"' ";
            var largo = Raro() ? max + 1 : rnd.Next(obligatorio ? 1 : 0, max + 1);
            var sb = new StringBuilder();
            for (var i = 0; i < largo; i++)
                sb.Append(caracteres[rnd.Next(caracteres.Length)]);
            if (Raro() && sb.Length > 0)
                sb[0] = '\u0001';
            if (rnd.Next(10) == 0 && sb.Length > 1)
            {
                sb.Length--;
                sb.Append("🎂"); // un par sustituto: 2 caracteres en .NET, 1 en el XSD
            }
            var texto = sb.ToString();
            return obligatorio && string.IsNullOrWhiteSpace(texto) ? "x" : texto;
        }

        private decimal Monto(decimal max, int decimales = 2) =>
            Raro()
                ? rnd.Next(2) == 0 ? -1m : max * 1_000_000_000m
                : Math.Round((decimal)rnd.NextDouble() * max, rnd.Next(0, decimales + 2));

        private T De<T>(params T[] valores) => valores[rnd.Next(valores.Length)];

        public Cfe Cfe()
        {
            var tipo = De(Enum.GetValues<TipoCfe>());
            var familia = tipo.Familia();
            var exportacion = tipo.EsExportacion();
            var remito = tipo.EsRemito();
            var conMontos = !remito && familia != FamiliaCfe.Resguardo;
            var dolares = rnd.Next(4) == 0;

            return new Cfe(tipo)
            {
                FechaEmision = Hoy.AddDays(-rnd.Next(0, 3)),
                NroInterno = rnd.Next(4) == 0 ? Texto(50) : null,
                InfoAdicional = rnd.Next(4) == 0 ? Texto(150) : null,
                FechaVencimiento = rnd.Next(4) == 0 ? Hoy.AddDays(Raro() ? -5 : rnd.Next(0, 60)) : null,
                FormaPago = De(FormaPago.Contado, FormaPago.Credito),
                Moneda = dolares ? Moneda.DolarAmericano : Moneda.PesoUruguayo,
                TipoCambio = dolares && !Raro() ? 0.001m + Monto(100m, 3) : null,
                Receptor = !tipo.RequiereReceptor() && familia != FamiliaCfe.Remito && rnd.Next(2) == 0 ? null : new Receptor
                {
                    Documento = exportacion ? $"X{rnd.Next(1, 999_999)}"
                        : familia is FamiliaCfe.Factura or FamiliaCfe.Resguardo || rnd.Next(2) == 0 ? (Raro() ? "214844360019" : "214844360018")
                        : null,
                    TipoDocumento = exportacion ? TipoDocumentoReceptor.Pasaporte : TipoDocumentoReceptor.Rut,
                    CodigoPais = exportacion ? (Raro() ? "ZZ" : De("US", "AR", "BR", "ES", "CN")) : "UY",
                    RazonSocial = Texto(150, obligatorio: familia == FamiliaCfe.Remito),
                    Direccion = Texto(70, obligatorio: familia == FamiliaCfe.Remito),
                    Ciudad = Texto(30, obligatorio: familia == FamiliaCfe.Remito),
                    Departamento = rnd.Next(2) == 0 ? Texto(30) : null,
                    Pais = rnd.Next(3) == 0 ? Texto(30) : null,
                    CodigoPostal = rnd.Next(3) == 0 ? rnd.Next(0, Raro() ? 999_999 : 99_999).ToString() : null,
                    InfoAdicional = rnd.Next(3) == 0 ? Texto(150) : null,
                    CompraId = rnd.Next(3) == 0 ? Texto(50) : null,
                    LugarDestinoEntrega = rnd.Next(3) == 0 ? Texto(100) : null,
                },
                Mandante = tipo.EsCuentaAjena() || Raro()
                    ? new Mandante { Documento = Texto(20, true), Nombre = Texto(150, true), CodigoPais = Raro() ? "ZZ" : "UY" }
                    : null,
                IndTraslado = remito ? (Raro() ? (IndTraslado)9 : (IndTraslado)rnd.Next(1, 7)) : null,
                ClausulaVenta = exportacion ? (Raro() ? "FOB1" : De("FOB", "CIF", "N/A", "EXW")) : null,
                ModalidadVenta = exportacion ? (Raro() ? 5 : De(1, 2, 3, 4, 80, 90, 91, 99)) : null,
                ViaTransporte = exportacion ? (Raro() ? 4 : De(1, 2, 3, 9)) : null,
                Detalle = [.. Enumerable.Range(0, Raro() ? 0 : rnd.Next(1, 5)).Select(_ => Linea(tipo))],
                DescuentosRecargos = conMontos && rnd.Next(4) == 0
                    ?
                    [
                        new DescuentoRecargoGlobal
                        {
                            Glosa = Texto(100, true), Monto = 0.01m + Monto(30m), Tipo = De(TipoMovimientoGlobal.Descuento, TipoMovimientoGlobal.Recargo),
                            IndFact = (TipoIva)rnd.Next(1, 5), Codigo = rnd.Next(2) == 0 ? rnd.Next(0, 999) : null,
                        },
                    ]
                    : [],
                MediosPago = conMontos && rnd.Next(4) == 0
                    ? [new MedioPago { Glosa = Texto(100, true), Valor = Monto(300m), Orden = rnd.Next(2) == 0 ? rnd.Next(0, Raro() ? 120 : 99) : null }]
                    : [],
                Referencias = tipo.EsNotaCorreccion() || rnd.Next(5) == 0
                    ?
                    [
                        new RefCfe
                        {
                            TipoCfe = TipoCfe.ETicket, Serie = Raro() ? "a" : De("A", "AB", "1A"), NroCfe = Raro() ? 0 : rnd.Next(1, 9_999_999),
                            FechaCfe = Hoy.AddDays(-rnd.Next(3, 300)), MontoCfeRef = Raro() ? null : Monto(1000m),
                            MonedaCfeRef = Raro() ? null : Moneda.PesoUruguayo, Razon = rnd.Next(2) == 0 ? Texto(90) : null,
                            TipoCambioCfeRef = rnd.Next(5) == 0 ? 0.001m + Monto(50m, 3) : null,
                        },
                    ]
                    : [],
            };
        }

        private LineaDetalle Linea(TipoCfe tipo)
        {
            var remito = tipo.EsRemito();
            var resguardo = tipo.Familia() == FamiliaCfe.Resguardo;
            return new LineaDetalle
            {
                NombreItem = resguardo ? "" : Texto(80, true),
                Descripcion = rnd.Next(4) == 0 ? Texto(300) : null,
                UnidadMedida = rnd.Next(3) == 0 ? Texto(4) : null,
                Cantidad = 0.001m + Monto(1000m, 3),
                PrecioUnitario = tipo.Familia() == FamiliaCfe.Remito ? 0 : Monto(100_000m, 6),
                IndFactIva = Raro() ? (TipoIva)9 : (TipoIva)rnd.Next(1, 5),
                DescuentoMonto = !remito && rnd.Next(5) == 0 ? Monto(20m) : 0,
                DescuentoPorcentaje = !remito && rnd.Next(6) == 0 ? Monto(100m, 3) : null,
                RecargoMonto = !remito && rnd.Next(6) == 0 ? Monto(100m) : 0,
                Codigos = rnd.Next(4) == 0 ? [new CodigoItem { Tipo = Texto(10, true), Codigo = Texto(35, true) }] : [],
                Retenciones = resguardo || Raro()
                    ? [.. Enumerable.Range(0, Raro() ? 0 : rnd.Next(1, 4)).Select(_ => new RetencionPercepcion
                    {
                        Codigo = Texto(8, true), Tasa = rnd.Next(2) == 0 ? Monto(100m, 3) : null,
                        MontoSujeto = Monto(100_000m), Valor = Monto(10_000m), InfoAdicional = rnd.Next(3) == 0 ? Texto(150) : null,
                    })]
                    : [],
            };
        }
    }
}
