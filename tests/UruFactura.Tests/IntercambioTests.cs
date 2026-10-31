using System.Security.Cryptography.X509Certificates;
using System.Xml;
using UruFactura.Xml;

namespace UruFactura.Tests;

/// <summary>
/// Intercambio de CFE entre emisores electrónicos: sobre al receptor con adenda, controles y acuses del
/// receptor (ACKSobre y ACKCFE), comunicación de anulación y consulta del padrón, validados contra los XSD
/// oficiales (EnvioCFE_entreEmpresas, ACKSobre, ACKCFE_Partes y CFEs_Anulados_x_RechazoDGI).
/// </summary>
public class IntercambioTests
{
    /// <summary>RUT del cliente de los CFE de prueba (receptor electrónico).</summary>
    private const string RutReceptor = "214844360018";

    private static readonly X509Certificate2 CertificadoReceptor = TestData.CertificadoAutofirmado();

    /// <summary>El emisor de prueba, con CAE de e-Factura y de su nota de crédito.</summary>
    private static async Task<(UruFacturaClient Cliente, DgiStub Dgi)> Emisor()
    {
        var dgi = new DgiStub();
        var cliente = dgi.Cliente();
        await cliente.RegistrarCaeAsync(TestData.Cae(TipoCfe.EFactura));
        await cliente.RegistrarCaeAsync(TestData.Cae(TipoCfe.ERemitoContingencia));
        return (cliente, dgi);
    }

    /// <summary>El receptor: otra empresa (el cliente de las e-Facturas de prueba) con su propio certificado.</summary>
    private static UruFacturaClient Receptor(string rut = RutReceptor, DgiStub? dgi = null) =>
        (dgi ?? new DgiStub()).Cliente(config: TestData.Config() with
        {
            Emisor = TestData.Emisor() with { Rut = rut, RazonSocial = "Cliente S.A." },
            Certificado = CertificadoReceptor,
        });

    private static async Task<CfeEmitido> FacturaEnviada(UruFacturaClient cliente, string? adenda = "Orden de compra: OC-123")
    {
        var emitido = await cliente.EmitirAsync(TestData.CfeValido(TipoCfe.EFactura) with { Adenda = adenda });
        await cliente.EnviarAsync(emitido);
        return emitido;
    }

    private static XmlNamespaceManager Nsm(XmlDocument doc)
    {
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("c", "http://cfe.dgi.gub.uy");
        return nsm;
    }

    // ------------------------------------------------------------------ sobre al receptor

    [Fact]
    public async Task SobreReceptor_EsValidoContraElEsquema_ConLaAdendaFueraDeLaFirma()
    {
        var (emisor, _) = await Emisor();
        using var _ = emisor;
        var factura = await FacturaEnviada(emisor);

        var sobre = await emisor.GenerarSobreReceptorAsync([factura]);

        TestData.AssertValidoContraXsd(sobre.Xml);
        Assert.Equal(RutReceptor, sobre.RutReceptor);
        Assert.Equal($"Sob_219999820013_20260615_{sobre.IdEmisor}.xml", sobre.NombreArchivo);

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(sobre.Xml);
        Assert.Equal("EnvioCFE_entreEmpresas", doc.DocumentElement!.LocalName);
        Assert.Equal(RutReceptor, doc.SelectSingleNode("//c:Caratula/c:RutReceptor", Nsm(doc))!.InnerText);
        Assert.Equal("Orden de compra: OC-123", doc.SelectSingleNode("//c:CFE_Adenda/c:Adenda", Nsm(doc))!.InnerText);

        // El CFE sigue firmado tal cual: la adenda no forma parte de la firma.
        var cfe = new XmlDocument { PreserveWhitespace = true };
        cfe.AppendChild(cfe.ImportNode(doc.SelectSingleNode("//c:CFE_Adenda/c:CFE", Nsm(doc))!, true));
        Assert.NotNull(FirmaDigital.VerificarFirma(cfe.OuterXml));
        Assert.DoesNotContain("OC-123", factura.Xml);
    }

    [Fact]
    public async Task SobreReceptor_CfeQueNoSeEnvioALaDgi_LanzaInvalidOperationException()
    {
        var (emisor, _) = await Emisor();
        using var _ = emisor;
        var factura = await emisor.EmitirAsync(TestData.CfeValido(TipoCfe.EFactura));

        await Assert.ThrowsAsync<InvalidOperationException>(() => emisor.GenerarSobreReceptorAsync([factura]));
    }

    [Fact]
    public async Task SobreReceptor_Contingencia_NoSeEnviaAlReceptor()
    {
        var (emisor, _) = await Emisor();
        using var _ = emisor;
        var cfc = await emisor.EmitirAsync(TestData.CfeValido(TipoCfe.ERemitoContingencia) with
        {
            Receptor = new Receptor { Documento = RutReceptor, RazonSocial = "Cliente S.A.", Direccion = "Calle 1", Ciudad = "Montevideo" },
        });
        await emisor.EnviarAsync(cfc);

        var ex = await Assert.ThrowsAsync<UruFacturaException>(() => emisor.GenerarSobreReceptorAsync([cfc]));
        Assert.Contains("contingencia", ex.Message);
    }

    [Fact]
    public async Task SobreReceptor_CfeDeReceptoresDistintos_LanzaUruFacturaException()
    {
        var (emisor, _) = await Emisor();
        using var _ = emisor;
        var a = await FacturaEnviada(emisor);
        var b = await emisor.EmitirAsync(TestData.CfeValido(TipoCfe.EFactura) with { Receptor = TestData.ReceptorRut() with { Documento = "219999820013" } });
        await emisor.EnviarAsync(b);

        await Assert.ThrowsAsync<UruFacturaException>(() => emisor.GenerarSobreReceptorAsync([a, b]));
    }

    [Fact]
    public async Task SobreReceptor_CfeRechazadoPorLaDgi_NoSeEnvia()
    {
        var (emisor, dgi) = await Emisor();
        using var _ = emisor;
        var factura = await emisor.EmitirAsync(TestData.CfeValido(TipoCfe.EFactura));
        var acuse = await emisor.EnviarAsync(factura);
        dgi.EstadoPorNumero[factura.Numero] = EstadoCfe.Rechazado;
        await emisor.ConsultarEnvioAsync(acuse);

        await Assert.ThrowsAsync<UruFacturaException>(() => emisor.GenerarSobreReceptorAsync([factura]));
    }

    // ------------------------------------------------------------------ recepción y respuestas

    [Fact]
    public async Task RecibirSobre_DeUnEmisor_LoAcepta_ConElAcuseFirmadoYValido_QueElEmisorLee()
    {
        var (emisor, _) = await Emisor();
        using var _ = emisor;
        using var receptor = Receptor();
        var factura = await FacturaEnviada(emisor);
        var sobre = await emisor.GenerarSobreReceptorAsync([factura]);

        var recibido = await receptor.RecibirSobreAsync(sobre.Xml, sobre.NombreArchivo);

        Assert.True(recibido.Recibido, string.Join("; ", recibido.Motivos));
        Assert.Equal(("219999820013", sobre.IdEmisor), (recibido.RutEmisor, recibido.IdEmisor));
        var cfe = Assert.Single(recibido.Comprobantes);
        Assert.Equal(EstadoCfe.Aceptado, cfe.Estado);
        Assert.Equal((1, TipoCfe.EFactura, "A", factura.Numero), (cfe.NroOrdinal, cfe.Tipo!.Value, cfe.Serie!, cfe.Numero!.Value));
        Assert.Equal((TestData.Dia, "UYU", 244m), (cfe.FechaEmision!.Value, cfe.Moneda!, cfe.Monto!.Value));
        Assert.Equal("Orden de compra: OC-123", cfe.Adenda);
        Assert.NotNull(FirmaDigital.VerificarFirma(cfe.Xml));

        TestData.AssertValidoContraXsd(recibido.XmlAcuse);
        Assert.NotNull(FirmaDigital.VerificarFirma(recibido.XmlAcuse));
        Assert.StartsWith($"M_{recibido.IdReceptor}_Sob_219999820013_20260615_{sobre.IdEmisor}", recibido.NombreArchivoAcuse);

        var acuse = emisor.LeerAcuseReceptor(recibido.XmlAcuse);
        Assert.True(acuse.Recibido);
        Assert.Equal((RutReceptor, sobre.IdEmisor, recibido.IdReceptor.ToString()), (acuse.RutReceptor, acuse.IdEmisor!.Value, acuse.IdReceptor));
    }

    [Fact]
    public async Task ResponderCfes_ConRechazoComercial_EsValidaYElEmisorLaLee()
    {
        var (emisor, _) = await Emisor();
        using var _ = emisor;
        using var receptor = Receptor();
        var sobre = await emisor.GenerarSobreReceptorAsync([await FacturaEnviada(emisor), await FacturaEnviada(emisor, adenda: null)]);
        var recibido = await receptor.RecibirSobreAsync(sobre.Xml, sobre.NombreArchivo);

        var respuesta = await receptor.ResponderCfesAsync(recibido, new Dictionary<int, IReadOnlyList<MotivoRechazo>>
        {
            [2] = [MotivoRechazo.DiferenciaDePrecios("No es el precio de lista")],
        });

        TestData.AssertValidoContraXsd(respuesta.Xml);
        Assert.NotNull(FirmaDigital.VerificarFirma(respuesta.Xml));
        Assert.StartsWith("ME_", respuesta.NombreArchivo);
        Assert.Equal([EstadoCfe.Aceptado, EstadoCfe.Rechazado], respuesta.Comprobantes.Select(c => c.Estado));

        var estado = emisor.LeerRespuestaReceptor(respuesta.Xml);
        Assert.Equal(sobre.IdEmisor, estado.IdEmisor);
        Assert.Equal([EstadoCfe.Aceptado, EstadoCfe.Rechazado], estado.Comprobantes.Select(c => c.Estado));
        Assert.Contains("E24", Assert.Single(estado.Comprobantes[1].Motivos));
    }

    [Theory]
    [InlineData("E01")] // solo de la DGI
    [InlineData("E15")]
    [InlineData("S02")]
    [InlineData("E61")]
    public async Task ResponderCfes_MotivoQueNoUsaUnReceptor_LanzaArgumentException(string codigo)
    {
        var (emisor, _) = await Emisor();
        using var _ = emisor;
        using var receptor = Receptor();
        var recibido = await receptor.RecibirSobreAsync((await emisor.GenerarSobreReceptorAsync([await FacturaEnviada(emisor)])).Xml);

        await Assert.ThrowsAsync<ArgumentException>(() => receptor.ResponderCfesAsync(recibido,
            new Dictionary<int, IReadOnlyList<MotivoRechazo>> { [1] = [new MotivoRechazo(codigo, "Motivo")] }));
    }

    [Fact]
    public async Task ResponderCfes_SobreRechazado_LanzaInvalidOperationException()
    {
        var (emisor, _) = await Emisor();
        using var _ = emisor;
        using var otro = Receptor("219999820013"); // el sobre no es para él
        var recibido = await otro.RecibirSobreAsync((await emisor.GenerarSobreReceptorAsync([await FacturaEnviada(emisor)])).Xml);

        await Assert.ThrowsAsync<InvalidOperationException>(() => otro.ResponderCfesAsync(recibido));
    }

    // ------------------------------------------------------------------ controles del sobre (S01 a S07)

    private static async Task<(UruFacturaClient Emisor, SobreReceptor Sobre)> SobreParaElReceptor()
    {
        var (emisor, _) = await Emisor();
        return (emisor, await emisor.GenerarSobreReceptorAsync([await FacturaEnviada(emisor)]));
    }

    [Fact]
    public async Task RecibirSobre_DirigidoAOtroRut_LoRechazaConS02_YElEmisorLeeElRechazo()
    {
        var (emisor, sobre) = await SobreParaElReceptor();
        using var _ = emisor;
        using var otro = Receptor("219999820013");

        var recibido = await otro.RecibirSobreAsync(sobre.Xml);

        Assert.False(recibido.Recibido);
        Assert.Contains("S02", recibido.Motivos.Select(m => m.Codigo));
        Assert.Empty(recibido.Comprobantes);
        TestData.AssertValidoContraXsd(recibido.XmlAcuse);
    }

    [Fact]
    public async Task RecibirSobre_CantidadQueNoCoincideConLaCaratula_LoRechazaConS05()
    {
        var (emisor, sobre) = await SobreParaElReceptor();
        using var _ = emisor;
        using var receptor = Receptor();

        var recibido = await receptor.RecibirSobreAsync(sobre.Xml.Replace("<CantCFE>1</CantCFE>", "<CantCFE>2</CantCFE>"));

        Assert.Equal(["S05"], recibido.Motivos.Select(m => m.Codigo));
    }

    [Fact]
    public async Task RecibirSobre_CaratulaConOtroCertificadoQueElDeLosCfe_LoRechazaConS06()
    {
        var (emisor, sobre) = await SobreParaElReceptor();
        using var _ = emisor;
        using var receptor = Receptor();
        // Solo el de la carátula, que es el primero (el del CFE está en su firma).
        var certificado = Convert.ToBase64String(TestData.Certificado.RawData);
        var i = sobre.Xml.IndexOf(certificado, StringComparison.Ordinal);
        var otro = sobre.Xml[..i] + Convert.ToBase64String(CertificadoReceptor.RawData) + sobre.Xml[(i + certificado.Length)..];

        var recibido = await receptor.RecibirSobreAsync(otro);

        Assert.Contains("S06", recibido.Motivos.Select(m => m.Codigo));
    }

    [Fact]
    public async Task RecibirSobre_CfeConFirmaSinCertificado_SeVerificaConElDeLaCaratula()
    {
        var (emisor, sobre) = await SobreParaElReceptor();
        using var _ = emisor;
        using var receptor = Receptor();
        // El certificado de la firma del CFE (el segundo) se quita: queda X509IssuerSerial, como en el ejemplo de la DGI.
        var certificado = $"<X509Certificate>{Convert.ToBase64String(TestData.Certificado.RawData)}</X509Certificate>";
        var sinCertificado = sobre.Xml.Remove(sobre.Xml.LastIndexOf(certificado, StringComparison.Ordinal), certificado.Length);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sinCertificado, "<X509Certificate>"));

        var recibido = await receptor.RecibirSobreAsync(sinCertificado);

        Assert.True(recibido.Recibido, string.Join("; ", recibido.Motivos));
        Assert.Equal(EstadoCfe.Aceptado, Assert.Single(recibido.Comprobantes).Estado);
    }

    [Fact]
    public async Task RecibirSobre_DelFormatoParaLaDgi_LoRechazaConS01()
    {
        using var receptor = Receptor();
        var sobreDgi = CfeSobre.Generar("219999820013", 5, DateTimeOffset.UtcNow, TestData.Certificado.RawData, [TestData.Emitido(TestData.CfeValido(TipoCfe.EFactura)).Xml]);

        var recibido = await receptor.RecibirSobreAsync(sobreDgi);

        Assert.Contains("S01", recibido.Motivos.Select(m => m.Codigo));
    }

    [Theory]
    [InlineData("no es xml")]
    [InlineData("<EnvioCFE_entreEmpresas xmlns=\"http://cfe.dgi.gub.uy\" version=\"1.0\"/>")]
    public async Task RecibirSobre_SinCaratulaParaResponder_LanzaUruFacturaException(string xml)
    {
        using var receptor = Receptor();
        await Assert.ThrowsAsync<UruFacturaException>(() => receptor.RecibirSobreAsync(xml));
    }

    // ------------------------------------------------------------------ controles de cada CFE (E02 a E07)

    private static string SobreCon(params string[] cfes) =>
        CfeSobre.GenerarParaReceptor(RutReceptor, "219999820013", 77, new DateTimeOffset(2026, 6, 15, 12, 0, 0, HoraUruguay.Offset),
            TestData.Certificado.RawData, [.. cfes.Select(xml => (xml, (string?)null))]);

    [Fact]
    public async Task RecibirSobre_CfeAdulterado_LoRechazaConE04()
    {
        using var receptor = Receptor();
        var xml = TestData.Emitido(TestData.CfeValido(TipoCfe.EFactura)).Xml.Replace("Servicio de consultoría", "Servicio de consultoria");

        var cfe = Assert.Single((await receptor.RecibirSobreAsync(SobreCon(xml))).Comprobantes);

        Assert.Equal(EstadoCfe.Rechazado, cfe.Estado);
        Assert.Equal(["E04"], cfe.Motivos.Select(m => m.Codigo));
    }

    [Fact]
    public async Task RecibirSobre_NumeroFueraDelRangoDelCae_LoRechazaConE03()
    {
        using var receptor = Receptor();
        var xml = TestData.Emitido(TestData.CfeValido(TipoCfe.EFactura), numero: 9, cae: TestData.Cae(TipoCfe.EFactura, hasta: 5)).Xml;

        var cfe = Assert.Single((await receptor.RecibirSobreAsync(SobreCon(xml))).Comprobantes);

        Assert.Equal(["E03"], cfe.Motivos.Select(m => m.Codigo));
    }

    [Fact]
    public async Task RecibirSobre_FirmadoDespuesDelVencimientoDelCae_LoRechazaConE07()
    {
        using var receptor = Receptor();
        var xml = TestData.Emitido(TestData.CfeValido(TipoCfe.EFactura), cae: TestData.Cae(TipoCfe.EFactura, vence: TestData.Dia.AddDays(-1))).Xml;

        var cfe = Assert.Single((await receptor.RecibirSobreAsync(SobreCon(xml))).Comprobantes);

        Assert.Equal(["E07"], cfe.Motivos.Select(m => m.Codigo));
    }

    [Fact]
    public async Task RecibirSobre_CfeRepetidoEnElSobre_RechazaElSegundoConE02()
    {
        using var receptor = Receptor();
        var xml = TestData.Emitido(TestData.CfeValido(TipoCfe.EFactura)).Xml;

        var recibido = await receptor.RecibirSobreAsync(SobreCon(xml, xml));

        Assert.Equal([EstadoCfe.Aceptado, EstadoCfe.Rechazado], recibido.Comprobantes.Select(c => c.Estado));
        Assert.Equal("E02", Assert.Single(recibido.Comprobantes[1].Motivos).Codigo);

        // La respuesta con los rechazos de los controles también es válida.
        TestData.AssertValidoContraXsd((await receptor.ResponderCfesAsync(recibido)).Xml);
    }

    // ------------------------------------------------------------------ lectura de los mensajes del receptor

    [Fact]
    public async Task LeerAcuseReceptor_ConLaFirmaAdulterada_LanzaUruFacturaException()
    {
        var (emisor, sobre) = await SobreParaElReceptor();
        using var _ = emisor;
        using var receptor = Receptor();
        var recibido = await receptor.RecibirSobreAsync(sobre.Xml);

        Assert.Throws<UruFacturaException>(() => emisor.LeerAcuseReceptor(recibido.XmlAcuse.Replace("<Estado>AS</Estado>", "<Estado>BS</Estado>")));
    }

    [Fact]
    public async Task LeerAcuseReceptor_DeUnSobreDeOtroEmisor_LanzaUruFacturaException()
    {
        var (emisor, sobre) = await SobreParaElReceptor();
        using var _ = emisor;
        using var receptor = Receptor();
        var recibido = await receptor.RecibirSobreAsync(sobre.Xml);

        // El acuse es para el emisor del sobre: otro (aquí, el propio receptor) no lo acepta como suyo.
        Assert.Throws<UruFacturaException>(() => receptor.LeerAcuseReceptor(recibido.XmlAcuse));
    }

    // ------------------------------------------------------------------ anulación por rechazo de la DGI

    [Fact]
    public async Task NotificacionAnulacion_DeUnCfeRechazado_EsValidaYElReceptorLaLee()
    {
        var (emisor, dgi) = await Emisor();
        using var _ = emisor;
        using var receptor = Receptor();
        var factura = await emisor.EmitirAsync(TestData.CfeValido(TipoCfe.EFactura));
        var acuse = await emisor.EnviarAsync(factura);
        dgi.EstadoPorNumero[factura.Numero] = EstadoCfe.Rechazado;
        await emisor.ConsultarEnvioAsync(acuse);
        Assert.True(factura.AnuladoPorRechazo);

        var notificacion = await emisor.GenerarNotificacionAnulacionAsync([factura]);

        TestData.AssertValidoContraXsd(notificacion.Xml);
        Assert.NotNull(FirmaDigital.VerificarFirma(notificacion.Xml));
        Assert.Equal($"Rech_219999820013_{notificacion.IdComunicacion}.xml", notificacion.NombreArchivo);
        Assert.Equal(
            [new CfeAnulado("219999820013", TipoCfe.EFactura, "A", factura.Numero, TestData.Dia)],
            receptor.LeerNotificacionAnulacion(notificacion.Xml));
    }

    [Fact]
    public async Task NotificacionAnulacion_DeUnCfeVigente_LanzaInvalidOperationException()
    {
        var (emisor, _) = await Emisor();
        using var _ = emisor;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await emisor.GenerarNotificacionAnulacionAsync([await FacturaEnviada(emisor)]));
    }

    [Theory]
    [InlineData("E05 No cumple validaciones", true)]
    [InlineData("E02 Tipo y Nº de CFE ya existe en los registros", false)] // un reenvío de un CFE recibido
    [InlineData("E09 RUC emisor y/o tipo de CFE no se corresponden con el CAE", false)]
    public void AnuladoPorRechazo_SalvoE02YE09(string motivo, bool anulado)
    {
        var cfe = TestData.Emitido();
        var rechazado = new CfeEmitido(cfe.Documento, cfe.Cae, cfe.Numero, cfe.Totales, cfe.FechaFirma, cfe.CodigoSeguridad, cfe.Xml)
        {
            EstadoDgi = EstadoCfe.Rechazado,
            MensajeDgi = motivo,
        };

        Assert.Equal(anulado, rechazado.AnuladoPorRechazo);
    }

    // ------------------------------------------------------------------ padrón y transporte

    [Fact]
    public async Task ConsultarEmisorElectronico_DevuelveElRutExacto()
    {
        var dgi = new DgiStub();
        dgi.Emisores.Add(new EmisorElectronico { Rut = "214844360018", Correo = "cfe@cliente.example" });
        dgi.Emisores.Add(new EmisorElectronico { Rut = "121484436001" });
        using var cliente = dgi.Cliente();

        Assert.Equal("cfe@cliente.example", (await cliente.ConsultarEmisorElectronicoAsync("214844360018"))!.Correo);
        Assert.Null(await cliente.ConsultarEmisorElectronicoAsync("219999820013"));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.ConsultarEmisorElectronicoAsync("123"));
    }

    [Fact]
    public async Task EnviarIntercambio_UsaElTransporte()
    {
        var dgi = new DgiStub();
        using var cliente = dgi.Cliente();

        var respuesta = await cliente.EnviarIntercambioAsync("https://receptor.example/ws", OperacionIntercambio.Sobre, "<x/>");

        Assert.True(respuesta.Recibido);
        Assert.Equal(("https://receptor.example/ws", OperacionIntercambio.Sobre, "<x/>"), Assert.Single(dgi.Intercambios));
    }
}
