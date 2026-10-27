using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using UruFactura.Soap;

namespace UruFactura.Tests;

public class DgiSoapClientTests
{
    private const string WsseNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private const string WsuNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

    private readonly X509Certificate2 _cert = TestData.Certificado;

    private static UruFacturaConfig Config(AlgoritmoFirma algoritmo = AlgoritmoFirma.Sha256) =>
        TestData.Config() with { AlgoritmoFirma = algoritmo };

    private DgiSoapClient Cliente(HttpMessageHandler handler, UruFacturaConfig? config = null) =>
        new(config ?? Config(), _cert, Http(handler));

    private static HttpClient Http(HttpMessageHandler handler) =>
        new(handler) { Timeout = TimeSpan.FromSeconds(30) };

    private static string Soap(string xmlData, string operacion) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/">
          <SOAP-ENV:Body>
            <{{operacion}}Response xmlns="http://dgi.gub.uy"><Dataout><xmlData><![CDATA[{{xmlData}}]]></xmlData></Dataout></{{operacion}}Response>
          </SOAP-ENV:Body>
        </SOAP-ENV:Envelope>
        """;

    private const string AckSobreRecibido = """
        <ACKSobre xmlns="http://cfe.dgi.gub.uy" version="1.0"><Caratula><IDReceptor>987654</IDReceptor></Caratula>
        <Detalle><Estado>AS</Estado><ParamConsulta><Token>dG9rZW4=</Token><Fechahora>2026-06-15T10:00:00-03:00</Fechahora></ParamConsulta></Detalle></ACKSobre>
        """;

    // ------------------------------------------------------------------ constructor

    [Fact]
    public void Constructor_HttpClientExternoConTimeoutInfinito_LanzaArgumentException()
    {
        using var httpClient = new HttpClient(new CapturingHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        Assert.Throws<ArgumentException>(() => new DgiSoapClient(Config(), _cert, httpClient));
    }

    [Fact]
    public void Constructor_OmitirValidacionSslTrueConHttpClientExterno_NoLanzaExcepcion()
    {
        using var httpClient = Http(new CapturingHandler());
        var ex = Record.Exception(() =>
        {
            using var _ = new DgiSoapClient(Config() with { OmitirValidacionSsl = true }, _cert, httpClient);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CuandoHttpClientEsExterno_NoLoDispone()
    {
        var handler = new CapturingHandler();
        using var httpClient = Http(handler);
        new DgiSoapClient(Config(), _cert, httpClient).Dispose();
        Assert.False(handler.Disposed);
    }

    // ------------------------------------------------------------------ EnviarSobre

    [Fact]
    public async Task EnviarSobre_ArmaElMensajeSegunElWsdlDeLaDgi()
    {
        var handler = new CapturingHandler { Respuesta = Soap(AckSobreRecibido, "WS_eFactura.EFACRECEPCIONSOBRE") };
        using var soap = Cliente(handler);

        await soap.EnviarSobreAsync("<EnvioCFE xmlns=\"http://cfe.dgi.gub.uy\"/>");

        Assert.Equal("https://efactura.dgi.gub.uy:6443/ePrueba/ws_eprueba", handler.Url);
        Assert.Equal("\"http://dgi.gub.uyaction/AWS_EFACTURA.EFACRECEPCIONSOBRE\"",
            handler.Headers["SOAPAction"]);
        Assert.StartsWith("text/xml", handler.ContentType);

        var doc = new XmlDocument();
        doc.LoadXml(handler.Cuerpo!);
        var nsm = Nsm(doc);
        Assert.NotNull(doc.SelectSingleNode("/soapenv:Envelope/soapenv:Body/dgi:WS_eFactura.EFACRECEPCIONSOBRE/dgi:Datain/dgi:xmlData", nsm));
        Assert.Equal("<EnvioCFE xmlns=\"http://cfe.dgi.gub.uy\"/>",
            doc.SelectSingleNode("//dgi:xmlData", nsm)!.InnerText);
        Assert.Contains("<![CDATA[<EnvioCFE", handler.Cuerpo);
    }

    private static XmlNamespaceManager Nsm(XmlDocument doc)
    {
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("soapenv", "http://schemas.xmlsoap.org/soap/envelope/");
        nsm.AddNamespace("dgi", "http://dgi.gub.uy");
        nsm.AddNamespace("wsse", WsseNs);
        nsm.AddNamespace("wsu", WsuNs);
        nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
        return nsm;
    }

    [Theory]
    [InlineData(AlgoritmoFirma.Sha256)]
    [InlineData(AlgoritmoFirma.Sha1)]
    public async Task EnviarSobre_FirmaElBodyConWsSecurity_YLaFirmaEsVerificable(AlgoritmoFirma algoritmo)
    {
        var handler = new CapturingHandler { Respuesta = Soap(AckSobreRecibido, "WS_eFactura.EFACRECEPCIONSOBRE") };
        using var soap = Cliente(handler, Config(algoritmo));

        await soap.EnviarSobreAsync("<x/>");

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(handler.Cuerpo!);
        var nsm = Nsm(doc);

        var security = doc.SelectSingleNode("/soapenv:Envelope/soapenv:Header/wsse:Security", nsm)!;
        Assert.Equal("1", security.Attributes!["mustUnderstand", "http://schemas.xmlsoap.org/soap/envelope/"]!.Value);

        var token = security.SelectSingleNode("wsse:BinarySecurityToken", nsm)!;
        Assert.Equal(Convert.ToBase64String(_cert.RawData), token.InnerText);
        var tokenId = token.Attributes!["Id", WsuNs]!.Value;

        var body = doc.SelectSingleNode("/soapenv:Envelope/soapenv:Body", nsm)!;
        var bodyId = body.Attributes!["Id", WsuNs]!.Value;

        var reference = security.SelectSingleNode("ds:Signature/ds:SignedInfo/ds:Reference", nsm)!;
        Assert.Equal("#" + bodyId, reference.Attributes!["URI"]!.Value);
        Assert.Equal("#" + tokenId,
            security.SelectSingleNode("ds:Signature/ds:KeyInfo/wsse:SecurityTokenReference/wsse:Reference/@URI", nsm)!.Value);
        Assert.Contains(algoritmo == AlgoritmoFirma.Sha1 ? "rsa-sha1" : "rsa-sha256", handler.Cuerpo);

        // La firma verifica contra el certificado y se rompe si se altera el Body.
        Assert.True(VerificarWss(doc, nsm));
        body.InnerXml = body.InnerXml.Replace("<x/>", "<y/>");
        var body2 = body.SelectSingleNode(".//dgi:xmlData", nsm)!;
        body2.InnerText = "alterado";
        Assert.False(VerificarWss(doc, nsm));
    }

    private bool VerificarWss(XmlDocument doc, XmlNamespaceManager nsm)
    {
        var firma = (XmlElement)doc.SelectSingleNode("//ds:Signature", nsm)!;
        var signed = new SignedXmlWsuVerificador(doc);
        signed.LoadXml(firma);
        using var key = _cert.GetRSAPublicKey()!;
        return signed.CheckSignature(key);
    }

    private sealed class SignedXmlWsuVerificador(XmlDocument doc) : SignedXml(doc)
    {
        public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
        {
            var nsm = new XmlNamespaceManager(document!.NameTable);
            nsm.AddNamespace("wsu", WsuNs);
            return (XmlElement?)document.SelectSingleNode($"//*[@wsu:Id='{idValue}']", nsm)
                   ?? base.GetIdElement(document, idValue);
        }
    }

    [Fact]
    public async Task EnviarSobre_Recibido_ExtraeIdReceptorYTokenDelAckSobre()
    {
        var handler = new CapturingHandler { Respuesta = Soap(AckSobreRecibido, "WS_eFactura.EFACRECEPCIONSOBRE") };
        using var soap = Cliente(handler);

        var r = await soap.EnviarSobreAsync("<x/>");

        Assert.True(r.Recibido);
        Assert.Equal(EstadoSobre.Recibido, r.Estado);
        Assert.Equal("987654", r.IdReceptor);
        Assert.Equal("dG9rZW4=", r.Token);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.FromHours(-3)), r.ConsultarDesde);
    }

    [Fact]
    public async Task EnviarSobre_Rechazado_InformaMotivos()
    {
        var ack = """
            <ACKSobre xmlns="http://cfe.dgi.gub.uy"><Caratula><IDReceptor>1</IDReceptor></Caratula>
            <Detalle><Estado>BS</Estado><MotivosRechazo><Motivo>S03</Motivo><Glosa>Certificado inválido</Glosa></MotivosRechazo></Detalle></ACKSobre>
            """;
        var handler = new CapturingHandler { Respuesta = Soap(ack, "WS_eFactura.EFACRECEPCIONSOBRE") };
        using var soap = Cliente(handler);

        var r = await soap.EnviarSobreAsync("<x/>");

        Assert.False(r.Recibido);
        Assert.Equal(EstadoSobre.Rechazado, r.Estado);
        Assert.Contains("S03", r.Mensaje);
        Assert.Equal(["S03 Certificado inválido"], r.Motivos);
    }

    [Fact]
    public async Task EnviarSobre_SoapFault_LanzaDgiCommunicationException()
    {
        var fault = """
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"><soapenv:Body>
            <soapenv:Fault><faultcode>soapenv:Server</faultcode><faultstring>Firma inválida</faultstring></soapenv:Fault>
            </soapenv:Body></soapenv:Envelope>
            """;
        var handler = new CapturingHandler { Respuesta = fault, Status = HttpStatusCode.InternalServerError };
        using var soap = Cliente(handler);

        var ex = await Assert.ThrowsAsync<DgiCommunicationException>(() => soap.EnviarSobreAsync("<x/>"));

        Assert.Equal("SOAP_FAULT", ex.CodigoRespuesta);
        Assert.Contains("Firma inválida", ex.Message);
        Assert.Contains("Fault", ex.Respuesta);
    }

    [Theory]
    [InlineData("Bad Gateway")]
    [InlineData("<html><body><h1>502 Bad Gateway</h1></body></html>")]
    [InlineData("<!DOCTYPE html><html><body>Bad Gateway</body></html>")]
    public async Task EnviarSobre_Http502SinSoap_LanzaConElCodigoHttp(string cuerpo)
    {
        var handler = new CapturingHandler { Respuesta = cuerpo, Status = HttpStatusCode.BadGateway };
        using var soap = Cliente(handler);

        var ex = await Assert.ThrowsAsync<DgiCommunicationException>(() => soap.EnviarSobreAsync("<x/>"));

        Assert.Equal("502", ex.CodigoRespuesta);
        Assert.Equal(cuerpo, ex.Respuesta);
    }

    [Fact]
    public void ProcesarSobre_ConDtd_SeRechaza()
    {
        const string conDtd = """
            <?xml version="1.0"?>
            <!DOCTYPE x [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">]>
            <x>&b;</x>
            """;
        Assert.Throws<DgiCommunicationException>(() => DgiRespuestaParser.ProcesarSobre(conDtd));
    }

    [Theory]
    [InlineData("2026-06-15T10:00:00", "2026-06-15T10:00:00-03:00")]
    [InlineData("2026-06-15T13:00:00Z", "2026-06-15T13:00:00+00:00")]
    [InlineData("2026-06-15T10:00:00-03:00", "2026-06-15T10:00:00-03:00")]
    public void FechaHora_SinOffsetEsHoraDeUruguay(string texto, string esperado) =>
        Assert.Equal(DateTimeOffset.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture),
            DgiRespuestaParser.FechaHora(texto));

    [Fact]
    public async Task EnviarSobre_CancelacionDelUsuario_PropagaOperationCanceledException()
    {
        var handler = new CapturingHandler { Retardo = TimeSpan.FromSeconds(10) };
        using var soap = Cliente(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => soap.EnviarSobreAsync("<x/>", cts.Token));
    }

    [Fact]
    public async Task EnviarSobre_ErrorDeRed_LanzaDgiCommunicationException()
    {
        var handler = new CapturingHandler { Error = new HttpRequestException("sin red") };
        using var soap = Cliente(handler);

        var ex = await Assert.ThrowsAsync<DgiCommunicationException>(() => soap.EnviarSobreAsync("<x/>"));
        Assert.Contains("sin red", ex.Message);
    }

    [Fact]
    public async Task EnviarSobre_SeCortaLaLecturaDeLaRespuesta_LaLiberaYLanzaDgiCommunicationException()
    {
        var cuerpo = new CuerpoQueSeCorta();
        var handler = new CapturingHandler { Contenido = cuerpo };
        using var soap = Cliente(handler);

        await Assert.ThrowsAsync<DgiCommunicationException>(() => soap.EnviarSobreAsync("<x/>"));
        Assert.True(cuerpo.Liberado);
    }

    // ------------------------------------------------------------------ otras operaciones

    [Fact]
    public async Task ConsultarEstadoEnvio_EnviaConsultaCfeConIdReceptorYToken_YParseaElDetalle()
    {
        var ack = """
            <ACKCFE xmlns="http://cfe.dgi.gub.uy" version="1.0"><Caratula/>
            <ACKCFE_Det><Nro_ordinal>1</Nro_ordinal><TipoCFE>101</TipoCFE><Serie>A</Serie><NroCFE>1</NroCFE><Estado>AE</Estado></ACKCFE_Det>
            <ACKCFE_Det><Nro_ordinal>2</Nro_ordinal><TipoCFE>101</TipoCFE><Serie>A</Serie><NroCFE>2</NroCFE><Estado>BE</Estado>
              <MotivosRechazoCF><Motivo>E01</Motivo><Glosa>Número fuera de rango</Glosa></MotivosRechazoCF></ACKCFE_Det></ACKCFE>
            """;
        var handler = new CapturingHandler { Respuesta = Soap(ack, "WS_eFactura.EFACCONSULTARESTADOENVIO") };
        using var soap = Cliente(handler);

        var r = await soap.ConsultarEnvioAsync("987654", "dG9rZW4=");

        Assert.Equal("\"http://dgi.gub.uyaction/AWS_EFACTURA.EFACCONSULTARESTADOENVIO\"", handler.Headers["SOAPAction"]);
        var doc = new XmlDocument();
        doc.LoadXml(handler.Cuerpo!);
        var consulta = new XmlDocument();
        consulta.LoadXml(doc.SelectSingleNode("//dgi:xmlData", Nsm(doc))!.InnerText);
        Assert.Equal("987654", consulta.DocumentElement!["IdReceptor", "http://dgi.gub.uy"]!.InnerText);
        Assert.Equal("dG9rZW4=", consulta.DocumentElement["Token", "http://dgi.gub.uy"]!.InnerText);

        Assert.True(r.Procesado);
        Assert.False(r.TodosAceptados);
        Assert.Equal(2, r.Comprobantes.Count);
        Assert.Equal(EstadoCfe.Aceptado, r.Comprobantes[0].Estado);
        Assert.Equal(EstadoCfe.Rechazado, r.Comprobantes[1].Estado);
        Assert.Equal((2, 2L), (r.Comprobantes[1].NroOrdinal!.Value, r.Comprobantes[1].Numero!.Value));
        Assert.Contains("Número fuera de rango", r.Comprobantes[1].Motivos[0]);
    }

    [Fact]
    public async Task ConsultarEstadoEnvio_TodosAceptados_EsExitoso()
    {
        var ack = """
            <ACKCFE xmlns="http://cfe.dgi.gub.uy"><ACKCFE_Det><Nro_ordinal>1</Nro_ordinal><Estado>AE</Estado></ACKCFE_Det></ACKCFE>
            """;
        var handler = new CapturingHandler { Respuesta = Soap(ack, "WS_eFactura.EFACCONSULTARESTADOENVIO") };
        using var soap = Cliente(handler);

        var r = await soap.ConsultarEnvioAsync("1", "t");

        Assert.True(r.TodosAceptados);
        // Solo con Nro_ordinal (TipoCFE, Serie y NroCFE son opcionales): el cliente asocia por posición.
        var detalle = Assert.Single(r.Comprobantes);
        Assert.Equal(1, detalle.NroOrdinal);
        Assert.Null(detalle.Numero);
    }

    [Theory]
    [InlineData("<ACKSobre xmlns=\"http://cfe.dgi.gub.uy\"><Detalle><Estado>XX</Estado></Detalle></ACKSobre>", "WS_eFactura.EFACRECEPCIONSOBRE")]
    [InlineData("<ACKSobre xmlns=\"http://cfe.dgi.gub.uy\"><Detalle/></ACKSobre>", "WS_eFactura.EFACRECEPCIONSOBRE")]
    public async Task EnviarSobre_EstadoFueraDelEsquema_LanzaDgiCommunicationException(string ack, string operacion)
    {
        var handler = new CapturingHandler { Respuesta = Soap(ack, operacion) };
        using var soap = Cliente(handler);

        await Assert.ThrowsAsync<DgiCommunicationException>(() => soap.EnviarSobreAsync("<x/>"));
    }

    [Fact]
    public async Task ConsultarEstadoEnvio_EstadoFueraDelEsquema_LanzaDgiCommunicationException()
    {
        var ack = """
            <ACKCFE xmlns="http://cfe.dgi.gub.uy"><ACKCFE_Det><Nro_ordinal>1</Nro_ordinal><Estado>ZZ</Estado></ACKCFE_Det></ACKCFE>
            """;
        var handler = new CapturingHandler { Respuesta = Soap(ack, "WS_eFactura.EFACCONSULTARESTADOENVIO") };
        using var soap = Cliente(handler);

        await Assert.ThrowsAsync<DgiCommunicationException>(() => soap.ConsultarEnvioAsync("1", "t"));
    }

    [Fact]
    public async Task ConsultarEstadoCfe_UsaWsConsultasConCfeid()
    {
        var respuesta = """
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"><soapenv:Body>
            <WS_eFactura_Consultas.EFACCONSULTARESTADOCFEResponse xmlns="http://dgi.gub.uy"><Ackconsultaestadocfe>
            <EstadoCFE>AE</EstadoCFE><IdEmisor>2020</IdEmisor><IdReceptor>129</IdReceptor>
            <ParamConsulta><Token>t</Token><Fechahora>2012-10-19T11:43:38</Fechahora></ParamConsulta>
            </Ackconsultaestadocfe></WS_eFactura_Consultas.EFACCONSULTARESTADOCFEResponse></soapenv:Body></soapenv:Envelope>
            """;
        var handler = new CapturingHandler { Respuesta = respuesta };
        using var soap = Cliente(handler);

        var r = await soap.ConsultarCfeAsync(TipoCfe.ETicket, "A", 7);

        Assert.Equal("https://efactura.dgi.gub.uy:6460/ePrueba/ws_consultasPrueba", handler.Url);
        Assert.Equal("\"http://dgi.gub.uyaction/AWS_EFACTURA_CONSULTAS.EFACCONSULTARESTADOCFE\"", handler.Headers["SOAPAction"]);

        var doc = new XmlDocument();
        doc.LoadXml(handler.Cuerpo!);
        var nsm = Nsm(doc);
        var cfeid = "/soapenv:Envelope/soapenv:Body/dgi:WS_eFactura_Consultas.EFACCONSULTARESTADOCFE/dgi:Cfeid";
        Assert.Equal("101", doc.SelectSingleNode(cfeid + "/dgi:TipoCFE", nsm)!.InnerText);
        Assert.Equal("A", doc.SelectSingleNode(cfeid + "/dgi:Serie", nsm)!.InnerText);
        Assert.Equal("7", doc.SelectSingleNode(cfeid + "/dgi:Nro", nsm)!.InnerText);

        Assert.True(r.Aceptado);
        Assert.Equal(EstadoCfe.Aceptado, r.Estado);
        Assert.Equal((101, "A", 7L), (r.TipoCfe!.Value, r.Serie!, r.Numero!.Value));
    }

    [Fact]
    public async Task EnviarReporteDiario_UsaEfacRecepcionReporteYParseaAckRepDiario()
    {
        var ack = """
            <ACKRepDiario xmlns="http://cfe.dgi.gub.uy"><Caratula/><Detalle><Estado>AR</Estado></Detalle></ACKRepDiario>
            """;
        var handler = new CapturingHandler { Respuesta = Soap(ack, "WS_eFactura.EFACRECEPCIONREPORTE") };
        using var soap = Cliente(handler);

        var r = await soap.EnviarReporteAsync("<Reporte xmlns=\"http://cfe.dgi.gub.uy\"/>");

        Assert.Equal("\"http://dgi.gub.uyaction/AWS_EFACTURA.EFACRECEPCIONREPORTE\"", handler.Headers["SOAPAction"]);
        Assert.Contains("WS_eFactura.EFACRECEPCIONREPORTE", handler.Cuerpo);
        Assert.True(r.Recibido);
        Assert.Equal(EstadoReporte.Recibido, r.Estado);
    }

    [Fact]
    public async Task EnviarReporteDiario_Rechazado_NoEsExitoso()
    {
        var ack = """
            <ACKRepDiario xmlns="http://cfe.dgi.gub.uy"><Detalle><Estado>BR</Estado><MotivosRechazo><Motivo>R05</Motivo><Glosa>Fecha</Glosa></MotivosRechazo></Detalle></ACKRepDiario>
            """;
        var handler = new CapturingHandler { Respuesta = Soap(ack, "WS_eFactura.EFACRECEPCIONREPORTE") };
        using var soap = Cliente(handler);

        var r = await soap.EnviarReporteAsync("<x/>");

        Assert.False(r.Recibido);
        Assert.Equal(EstadoReporte.Rechazado, r.Estado);
        Assert.Contains("R05", r.Mensaje);
    }

    [Fact]
    public async Task Produccion_UsaLasUrlsConfiguradas()
    {
        var handler = new CapturingHandler { Respuesta = Soap(AckSobreRecibido, "WS_eFactura.EFACRECEPCIONSOBRE") };
        var config = Config() with
        {
            Ambiente = Ambiente.Produccion,
            UrlEnvio = "https://dgi.example/envio",
            UrlConsultas = "https://dgi.example/consultas",
        };
        using var soap = Cliente(handler, config);

        await soap.EnviarSobreAsync("<x/>");

        Assert.Equal("https://dgi.example/envio", handler.Url);
    }

    // ------------------------------------------------------------------ intercambio y padrón

    [Theory]
    [InlineData(OperacionIntercambio.Sobre, "SOBRE")]
    [InlineData(OperacionIntercambio.RespuestaSobre, "RESPUESTASOBRE")]
    [InlineData(OperacionIntercambio.RespuestaCfes, "RESPUESTACFES")]
    [InlineData(OperacionIntercambio.NotificacionAnulacion, "NOTIFICACIONANULACIONCFE")]
    public async Task EnviarIntercambio_ArmaElMensajeSegunElWsdlDeIntercambio_YLeeLaRespuesta(OperacionIntercambio operacion, string nombre)
    {
        // Los ejemplos de la DGI escriben codigo/descripcion/detalle en minúscula (el esquema, con mayúscula).
        var respuestas = """<Respuestas xmlns="http://cfe.dgi.gub.uy"><Respuesta><codigo>1000</codigo><descripcion>Sobre Recibido.</descripcion><detalle/></Respuesta></Respuestas>""";
        var handler = new CapturingHandler { Respuesta = Soap(respuestas, "WS_eFactura_Intercambio." + nombre) };
        using var soap = Cliente(handler);

        var r = await soap.EnviarIntercambioAsync("https://receptor.example/ws_efactura_intercambio", operacion, "<EnvioCFE_entreEmpresas/>");

        Assert.Equal("https://receptor.example/ws_efactura_intercambio", handler.Url);
        Assert.Equal($"\"http://dgi.gub.uyaction/AWS_EFACTURA_INTERCAMBIO.{nombre}\"", handler.Headers["SOAPAction"]);
        var doc = new XmlDocument();
        doc.LoadXml(handler.Cuerpo!);
        var xmlData = doc.SelectSingleNode($"/soapenv:Envelope/soapenv:Body/dgi:WS_eFactura_Intercambio.{nombre}/dgi:Datain/dgi:xmlData", Nsm(doc));
        Assert.Equal("<EnvioCFE_entreEmpresas/>", xmlData!.InnerText);
        Assert.NotNull(doc.SelectSingleNode("//wsse:Security/ds:Signature", Nsm(doc)));

        Assert.Equal(new RespuestaIntercambio(1000, "Sobre Recibido.", null), r);
        Assert.True(r.Recibido);
    }

    [Fact]
    public async Task EnviarIntercambio_ErrorDelReceptor_IndicaReintentar()
    {
        var respuestas = """<Respuestas><Respuesta><Codigo>1002</Codigo><Descripcion>Error al recibir el Sobre. Intente luego.</Descripcion><Detalle>mantenimiento</Detalle></Respuesta></Respuestas>""";
        var handler = new CapturingHandler { Respuesta = Soap(respuestas, "WS_eFactura_Intercambio.SOBRE") };
        using var soap = Cliente(handler);

        var r = await soap.EnviarIntercambioAsync("https://receptor.example/ws", OperacionIntercambio.Sobre, "<x/>");

        Assert.False(r.Recibido);
        Assert.True(r.Reintentar);
        Assert.Equal("mantenimiento", r.Detalle);
    }

    /// <summary>El padrón llega comprimido con gzip y en base64, como en el ejemplo del documento de servicios de consultas.</summary>
    private static string PadronComprimido(string xml)
    {
        using var ms = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(xml));
        return Convert.ToBase64String(ms.ToArray());
    }

    [Fact]
    public async Task ConsultarEmisores_UsaElPadronComprimidoConUrlDeServicio_YLoDescomprime()
    {
        var padron = """
            <RucEmisoresTransicionMailProv xmlns="http://dgi.gub.uy">
              <RucEmisoresTransicionMailProv.RucEmisoresTransicionMailProvItem>
                <RUC>219999830019</RUC><DENOMINACION>DGI RUC PRUEBA CEDE</DENOMINACION><FECHA_INICIO>2014-01-01</FECHA_INICIO>
                <FECHA_FIN_TRANSICION>2014-04-30</FECHA_FIN_TRANSICION><FECHA_FIN>0000-00-00</FECHA_FIN><MAIL>prueba@dgi.gub.uy</MAIL>
                <URL_WEBSERVICE>https://intercambio.example/ws_efactura_intercambio</URL_WEBSERVICE><MAIL_CONTACTO_TECNICO/>
              </RucEmisoresTransicionMailProv.RucEmisoresTransicionMailProvItem>
            </RucEmisoresTransicionMailProv>
            """;
        var respuesta = $"""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"><soapenv:Body>
            <WS_eFactura_Consultas.EFACCONSULTARRUCEMISORESTRANSICIONCOMPRIMIDOPROVResponse xmlns="http://dgi.gub.uy">
            <Rucemisorestransicionmailcomprimidoprov>{PadronComprimido(padron)}</Rucemisorestransicionmailcomprimidoprov>
            </WS_eFactura_Consultas.EFACCONSULTARRUCEMISORESTRANSICIONCOMPRIMIDOPROVResponse></soapenv:Body></soapenv:Envelope>
            """;
        var handler = new CapturingHandler { Respuesta = respuesta };
        using var soap = Cliente(handler);

        var emisores = await soap.ConsultarEmisoresAsync("219999830019");

        Assert.Equal("https://efactura.dgi.gub.uy:6460/ePrueba/ws_consultasPrueba", handler.Url);
        Assert.Equal("\"http://dgi.gub.uyaction/AWS_EFACTURA_CONSULTAS.EFACCONSULTARRUCEMISORESTRANSICIONCOMPRIMIDOPROV\"", handler.Headers["SOAPAction"]);
        var doc = new XmlDocument();
        doc.LoadXml(handler.Cuerpo!);
        var consulta = "/soapenv:Envelope/soapenv:Body/dgi:WS_eFactura_Consultas.EFACCONSULTARRUCEMISORESTRANSICIONCOMPRIMIDOPROV/dgi:Consultarucemisores";
        Assert.Equal("219999830019", doc.SelectSingleNode(consulta + "/dgi:Filtro_eFacDocNro", Nsm(doc))!.InnerText);
        Assert.NotNull(doc.SelectSingleNode(consulta + "/dgi:Filtro_eFacRUCEmisorAudFchHora", Nsm(doc)));

        var emisor = Assert.Single(emisores);
        Assert.Equal("219999830019", emisor.Rut);
        Assert.Equal("DGI RUC PRUEBA CEDE", emisor.Denominacion);
        Assert.Equal(new DateOnly(2014, 1, 1), emisor.FechaInicio);
        Assert.Null(emisor.FechaFin); // 0000-00-00
        Assert.Equal("prueba@dgi.gub.uy", emisor.Correo);
        Assert.Equal("https://intercambio.example/ws_efactura_intercambio", emisor.UrlServicioWeb);
        Assert.Null(emisor.CorreoContactoTecnico);
        Assert.True(emisor.EsEmisorEn(TestData.Dia));
    }

    // ------------------------------------------------------------------ handlers

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }
        public string? Url { get; private set; }
        public string? Cuerpo { get; private set; }
        public string? ContentType { get; private set; }
        public Dictionary<string, string> Headers { get; } = [];
        public string Respuesta { get; set; } = "<ok/>";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public TimeSpan Retardo { get; set; }
        public Exception? Error { get; set; }
        public HttpContent? Contenido { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            ContentType = request.Content?.Headers.ContentType?.ToString();
            foreach (var h in request.Headers)
                Headers[h.Key] = string.Join(",", h.Value);

            if (Error is not null)
                throw Error;
            if (Retardo > TimeSpan.Zero)
                await Task.Delay(Retardo, cancellationToken);

            return new HttpResponseMessage(Status) { Content = Contenido ?? new StringContent(Respuesta, Encoding.UTF8, "text/xml") };
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>Cuerpo cuya lectura falla a mitad (la conexión se corta).</summary>
    private sealed class CuerpoQueSeCorta : HttpContent
    {
        public bool Liberado { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            throw new IOException("La conexión se cerró.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Liberado = true;
            base.Dispose(disposing);
        }
    }
}
