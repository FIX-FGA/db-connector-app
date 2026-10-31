using System.Net.Sockets;
using DbConnector;
using DbConnector.Protocol;

namespace DbConnector.Tests;

public class ConectorTests
{
    private const string Exemplo = """
        {
          // comentários e vírgula sobrando são aceitos
          "nome": "ACME bancos",
          "emissor": "https://sso.acme.test/realms/interno/",
          "bancoPadrao": "app",
          "ambientes": [
            { "nome": "dev", "host": "dev.db.acme.test", "porta": 5432, "clientId": "db-dev", "portaLocal": 15432 },
            { "nome": "prod", "host": "db.acme.test", "porta": 5432, "clientId": "db-prod", "portaLocal": 15434, "somenteLeitura": true, "bancoPadrao": "app_ro" },
          ]
        }
        """;

    [Fact]
    public void Configuracao_vem_do_json_com_os_padroes_aplicados()
    {
        var config = Configuracao.Ler(Exemplo);

        Assert.Equal("ACME bancos", config.Nome);
        Assert.Equal("https://sso.acme.test/realms/interno", config.Emissor);
        Assert.Equal(15431, config.PortaRetorno);
        var dev = config.AmbientesEfetivos[0];
        Assert.Equal("app", dev.BancoPadrao);
        Assert.False(dev.SoLeitura);
        Assert.Equal("jdbc:postgresql://localhost:15432/app?sslmode=disable", dev.UrlJdbc);
        Assert.True(config.AmbientesEfetivos[1].SoLeitura);
        Assert.Equal("app_ro", config.AmbientesEfetivos[1].BancoPadrao);
    }

    [Fact]
    public void Sem_configuracao_nao_ha_ambientes()
    {
        var config = new Configuracao();

        Assert.Equal("DB Connector", config.Nome);
        Assert.Empty(config.AmbientesEfetivos);
        Assert.Empty(Opcoes.Ler([], config: config)!.Ambientes);
    }

    [Theory]
    [InlineData("""{ "ambientes": [ { "nome": "dev", "host": "h", "porta": 5432, "clientId": "c", "portaLocal": 15432 } ] }""")]
    [InlineData("""{ "emissor": "https://sso/r", "ambientes": [ { "nome": "a", "host": "h", "porta": 5432, "clientId": "c", "portaLocal": 15432 }, { "nome": "b", "host": "h", "porta": 5432, "clientId": "c", "portaLocal": 15432 } ] }""")]
    [InlineData("""{ "emissor": "https://sso/r", "ambientes": [ { "nome": "a", "host": "h", "porta": 5432, "clientId": "c", "portaLocal": 15431 } ] }""")]
    [InlineData("""{ "nome": """)]
    public void Configuracao_invalida_e_recusada(string json) =>
        Assert.Throws<ArgumentException>(() => Configuracao.Ler(json));

    [Fact]
    public void Argumentos_escolhem_os_ambientes_da_configuracao()
    {
        var config = Configuracao.Ler(Exemplo);

        Assert.Equal(["dev"], Opcoes.Ler([], config: config)!.Ambientes.Select(a => a.Nome));
        Assert.Empty(Opcoes.Ler([], nenhumSemArgumento: true, config: config)!.Ambientes);
        Assert.Equal(["prod", "dev"], Opcoes.Ler(["prod", "DEV", "dev"], config: config)!.Ambientes.Select(a => a.Nome));
        Assert.Null(Opcoes.Ler(["--ajuda"], config: config));
        Assert.Throws<ArgumentException>(() => Opcoes.Ler(["homolog"], config: config));
        Assert.Equal("microsoft", Opcoes.Ler(["--idp", "microsoft"], config: config)!.IdpHint);
    }

    [Fact]
    public void Gateway_avulso_substitui_os_ambientes()
    {
        var opcoes = Opcoes.Ler(["--gateway", "localhost:6432", "--client", "teste", "--inseguro"], config: Configuracao.Ler(Exemplo))!;

        var local = Assert.Single(opcoes.Ambientes);
        Assert.Equal(("localhost", 6432, "teste"), (local.Host, local.PortaGateway, local.ClientId));
        Assert.True(opcoes.AceitarCertificadoInvalido);
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
        var ambiente = new Ambiente("teste", "gateway.invalido", 5432, "db-teste", 25432);
        var opcoes = new Opcoes(new Configuracao(Nome: "Teste", Emissor: "https://sso.invalido/realms/r", PortaRetorno: 25431, Ambientes: [ambiente]), [ambiente]);
        using var http = new HttpClient();
        await using var conector = new Conector(opcoes, http, new SenhaLocal("AAAA-BBBB-CCCC"));
        var registros = new List<string>();
        conector.Registro += (_, mensagem) => { lock (registros) registros.Add(mensagem); };
        conector.Ligar(ambiente, entrarAgora: false);

        // Outra instância na mesma porta não abre.
        await using (var outro = new Conector(opcoes, http, new SenhaLocal()))
            Assert.Throws<InvalidOperationException>(() => outro.Ligar(ambiente, entrarAgora: false));

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
        Assert.Contains("Teste", PgWire.ParseErrorFields(erro.Value.Body)['M']);
        lock (registros) Assert.Contains(registros, r => r.Contains("senha local errada"));
    }
}
