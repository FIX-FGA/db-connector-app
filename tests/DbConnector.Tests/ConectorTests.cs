using System.Net.Sockets;
using DbConnector;
using DbConnector.Protocol;

namespace DbConnector.Tests;

public class ConectorTests
{
    private static readonly Conexao Dev = new("ACME dev", "https://sso.acme.test/realms/interno/", "db-dev", "dev.db.acme.test", 5432, 15432, "app");
    private static readonly Conexao Prod = new("ACME prod", "https://sso.acme.test/realms/interno", "db-prod", "db.acme.test", 5432, 15434, "app", SoLeitura: true, IdpHint: "microsoft");

    [Fact]
    public void Exporta_e_importa_as_conexoes_sem_perder_nada()
    {
        var json = ArquivoDeConexoes.Escrever([Dev, Prod]);
        var lidas = ArquivoDeConexoes.Ler(json);

        Assert.Equal([Dev, Prod], lidas);
        Assert.Contains("\"conexoes\"", json);
        Assert.Contains("\"somenteLeitura\": true", json);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://sso.acme.test/realms/interno", lidas[0].EmissorNormalizado);
        Assert.Equal("jdbc:postgresql://localhost:15432/app?sslmode=disable", lidas[0].UrlJdbc);
    }

    [Fact]
    public void Importa_o_formato_de_configuracao_antigo_uma_conexao_por_ambiente()
    {
        var lidas = ArquivoDeConexoes.Ler("""
            {
              // comentário e vírgula sobrando são aceitos
              "emissor": "https://sso.acme.test/realms/interno",
              "bancoPadrao": "app",
              "ambientes": [
                { "nome": "dev", "host": "dev.db.acme.test", "porta": 5432, "clientId": "db-dev", "portaLocal": 15432 },
                { "nome": "prod", "host": "db.acme.test", "porta": 6432, "clientId": "db-prod", "portaLocal": 15434, "somenteLeitura": true },
              ]
            }
            """);

        Assert.Equal(["dev", "prod"], lidas.Select(c => c.Nome));
        Assert.All(lidas, c => Assert.Equal("https://sso.acme.test/realms/interno", c.Emissor));
        Assert.Equal((6432, true, "app"), (lidas[1].PortaGateway, lidas[1].SoLeitura, lidas[1].BancoPadrao));
    }

    [Theory]
    [InlineData("""{ "conexoes": [ { "nome": "a", "emissor": "não é url", "clientId": "c", "host": "h" } ] }""")]
    [InlineData("""{ "conexoes": [ { "nome": "a", "emissor": "https://sso/r", "clientId": "c", "host": "h", "portaLocal": 15432 }, { "nome": "A", "emissor": "https://sso/r", "clientId": "c", "host": "h", "portaLocal": 15433 } ] }""")]
    [InlineData("""{ "conexoes": [ { "nome": "a", "emissor": "https://sso/r", "clientId": "c", "host": "h", "portaLocal": 15432 }, { "nome": "b", "emissor": "https://sso/r", "clientId": "c", "host": "h", "portaLocal": 15432 } ] }""")]
    [InlineData("""{ "conexoes": [ { "nome": "a", "emissor": "https://sso/r", "clientId": "c", "host": "h", "portaLocal": 15431 } ] }""")]
    [InlineData("""{ "outra": 1 }""")]
    [InlineData("""{ "conexoes": """)]
    public void Arquivo_invalido_e_recusado(string json) =>
        Assert.Throws<ArgumentException>(() => ArquivoDeConexoes.Ler(json));

    [Fact]
    public void Salva_e_carrega_do_disco()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"db-connector-{Guid.NewGuid():N}", "conexoes.json");
        try
        {
            Assert.Empty(ArquivoDeConexoes.Carregar(caminho));
            ArquivoDeConexoes.Salvar([Dev, Prod], caminho);
            Assert.Equal([Dev, Prod], ArquivoDeConexoes.Carregar(caminho));
            Assert.Throws<ArgumentException>(() => ArquivoDeConexoes.Salvar([Dev, Dev with { PortaLocal = 15500 }], caminho));
            Assert.Equal([Dev, Prod], ArquivoDeConexoes.Carregar(caminho));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(caminho)!, recursive: true);
        }
    }

    [Fact]
    public void Importar_substitui_pelo_nome_e_acha_porta_livre()
    {
        var importadas = new[] { Dev with { Host = "novo.db.acme.test" }, new Conexao("ACME qa", Dev.Emissor, "db-qa", "qa.db.acme.test", PortaLocal: 15434) };

        var juntas = ArquivoDeConexoes.Juntar([Dev, Prod], importadas);

        Assert.Equal(["ACME prod", "ACME dev", "ACME qa"], juntas.Select(c => c.Nome));
        Assert.Equal("novo.db.acme.test", juntas[1].Host);
        Assert.Equal(15435, juntas[2].PortaLocal);
        Assert.Empty(ArquivoDeConexoes.Validar(juntas));
        Assert.Equal(15433, ArquivoDeConexoes.PortaLivre([Dev, Prod]));
    }

    [Fact]
    public void Argumentos_escolhem_as_conexoes_pelo_nome()
    {
        IReadOnlyList<Conexao> salvas = [Dev, Prod];

        Assert.Equal(["ACME dev"], Opcoes.Ler([], salvas: salvas)!.Escolhidas.Select(c => c.Nome));
        Assert.Empty(Opcoes.Ler([], nenhumaSemArgumento: true, salvas: salvas)!.Escolhidas);
        Assert.Equal(["ACME prod", "ACME dev"], Opcoes.Ler(["acme prod", "ACME dev"], salvas: salvas)!.Escolhidas.Select(c => c.Nome));
        Assert.Null(Opcoes.Ler(["--ajuda"], salvas: salvas));
        Assert.Throws<ArgumentException>(() => Opcoes.Ler(["homolog"], salvas: salvas));
        var opcoes = Opcoes.Ler(["--idp", "google"], salvas: salvas)!;
        Assert.Equal("google", opcoes.IdpHintDe(Prod));
        Assert.Equal("microsoft", new Opcoes(salvas, []).IdpHintDe(Prod));
        Assert.Empty(Opcoes.Ler([], salvas: [])!.Escolhidas);
    }

    [Fact]
    public void Gateway_avulso_substitui_as_conexoes()
    {
        var opcoes = Opcoes.Ler(["--gateway", "localhost:6432", "--client", "teste", "--emissor", "http://localhost:8080/realms/r", "--inseguro"], salvas: [Dev])!;

        var local = Assert.Single(opcoes.Escolhidas);
        Assert.Equal(("localhost", 6432, "teste"), (local.Host, local.PortaGateway, local.ClientId));
        Assert.True(opcoes.AceitarCertificadoInvalido);
        Assert.Throws<ArgumentException>(() => Opcoes.Ler(["--gateway", "localhost:6432", "--client", "teste"], salvas: [Dev]));
    }

    [Fact]
    public async Task Senha_local_aceita_sem_hifens_e_em_minusculas()
    {
        var senha = new SenhaLocal();
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", senha.Valor);
        Assert.True(await senha.ConfereAsync(senha.Valor.Replace("-", "").ToLowerInvariant(), CancellationToken.None));
        Assert.False(await new SenhaLocal("AAAA-BBBB-CCCC").ConfereAsync("AAAA-BBBB-CCCD", CancellationToken.None));
    }

    [Fact]
    public async Task Senha_local_errada_e_recusada_antes_do_login_e_a_porta_fica_ocupada()
    {
        var conexao = new Conexao("teste", "https://sso.invalido/realms/r", "db-teste", "gateway.invalido", 5432, 25432);
        var opcoes = new Opcoes([conexao], [conexao]);
        using var http = new HttpClient();
        await using var conector = new Conector(opcoes, http, new SenhaLocal("AAAA-BBBB-CCCC"));
        var registros = new List<string>();
        conector.Registro += (_, mensagem) => { lock (registros) registros.Add(mensagem); };
        conector.Ligar(conexao, entrarAgora: false);

        // Outra instância na mesma porta não abre.
        await using (var outro = new Conector(opcoes, http, new SenhaLocal()))
            Assert.Throws<InvalidOperationException>(() => outro.Ligar(conexao, entrarAgora: false));

        using var cliente = new TcpClient();
        await cliente.ConnectAsync("127.0.0.1", 25432);
        var stream = cliente.GetStream();
        await stream.WriteAsync(PgWire.Startup(PgWire.ProtocolVersion3, PgWire.StartupParameters([new("user", "eu"), new("database", "app")])));
        var pedido = await PgWire.ReadAsync(stream, CancellationToken.None);
        Assert.Equal((byte)'R', pedido!.Value.Type);
        await PgWire.WriteAsync(stream, new PgMessage((byte)'p', [.. "errada"u8.ToArray(), 0]), CancellationToken.None);
        var erro = await PgWire.ReadAsync(stream, CancellationToken.None);

        Assert.Equal((byte)'E', erro!.Value.Type);
        Assert.Equal("28P01", PgWire.ParseErrorFields(erro.Value.Body)['C']);
        Assert.Contains("DB Connector", PgWire.ParseErrorFields(erro.Value.Body)['M']);
        lock (registros) Assert.Contains(registros, r => r.Contains("senha local errada"));
    }
}
