using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace DbConnector;

public enum Estado { Desligado, AguardandoLogin, Conectado, Falhou }

/// <summary>Como está um ambiente, para a janela mostrar.</summary>
public sealed record Situacao(Estado Estado, string? Email = null, DateTimeOffset? SessaoAte = null, int Conexoes = 0, string? Erro = null);

/// <summary>
/// Liga e desliga os ambientes: cada um ligado escuta na porta local dele, faz o login no navegador e atende as conexões
/// do DBeaver (<see cref="Sessao"/>). Os eventos vêm de qualquer thread.
/// </summary>
public sealed class Conector(Opcoes opcoes, HttpClient http, SenhaLocal senhaLocal) : IAsyncDisposable
{
    private readonly Opcoes _opcoes = opcoes;
    private readonly HttpClient _http = http;
    private readonly ConcurrentDictionary<string, Ligacao> _ligados = new(StringComparer.OrdinalIgnoreCase);

    public event Action<Ambiente, Situacao>? Mudou;
    public event Registrar? Registro;

    public SenhaLocal SenhaLocal => senhaLocal;

    public Situacao SituacaoDe(Ambiente ambiente) => _ligados.TryGetValue(ambiente.Nome, out var l) ? l.Situacao : new(Estado.Desligado);

    /// <summary>
    /// Abre a porta local e, com <paramref name="entrarAgora"/>, já começa o login (senão, só na primeira conexão do
    /// DBeaver). Falha (InvalidOperationException) se a porta já está em uso.
    /// </summary>
    public void Ligar(Ambiente ambiente, bool entrarAgora = true)
    {
        if (_ligados.ContainsKey(ambiente.Nome)) return;
        var escuta = new TcpListener(IPAddress.Loopback, ambiente.PortaLocal);
        try
        {
            escuta.Start();
        }
        catch (SocketException e)
        {
            throw new InvalidOperationException($"A porta {ambiente.PortaLocal} já está em uso: feche o outro {_opcoes.NomeDoAplicativo} (ou o programa que a usa) e conecte de novo.", e);
        }
        var ligacao = new Ligacao(this, ambiente, escuta);
        _ligados[ambiente.Nome] = ligacao;
        Registrar(ambiente, $"{ambiente.Host} → localhost:{ambiente.PortaLocal}");
        ligacao.Iniciar(entrarAgora);
    }

    /// <summary>Faz o login agora (reaproveita o token se ainda vale).</summary>
    public Task EntrarAsync(Ambiente ambiente) => _ligados.TryGetValue(ambiente.Nome, out var l) ? l.EntrarAsync() : Task.CompletedTask;

    /// <summary>Fecha a porta local e as conexões abertas e esquece o token.</summary>
    public async Task DesligarAsync(Ambiente ambiente)
    {
        if (!_ligados.TryRemove(ambiente.Nome, out var ligacao)) return;
        await ligacao.DisposeAsync();
        Registrar(ambiente, "desligado");
        Mudou?.Invoke(ambiente, new Situacao(Estado.Desligado));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var ligacao in _ligados.Values) await ligacao.DisposeAsync();
        _ligados.Clear();
    }

    private void Registrar(Ambiente ambiente, string mensagem) => Registro?.Invoke(ambiente, mensagem);

    private sealed class Ligacao : IAsyncDisposable
    {
        private readonly Conector _conector;
        private readonly Ambiente _ambiente;
        private readonly TcpListener _escuta;
        private readonly CancellationTokenSource _parar = new();
        private readonly TokenDoAmbiente _token;
        private readonly Sessao _sessao;
        private readonly object _trava = new();
        private Task _atendimento = Task.CompletedTask;
        private int _conexoes;

        public Ligacao(Conector conector, Ambiente ambiente, TcpListener escuta)
        {
            _conector = conector;
            _ambiente = ambiente;
            _escuta = escuta;
            Situacao = new Situacao(Estado.AguardandoLogin);
            var login = new LoginNavegador(conector._opcoes, conector._http, conector.Registrar);
            _token = new TokenDoAmbiente(ambiente, login);
            _token.LoginIniciado += () => Atualizar(s => s with { Estado = Estado.AguardandoLogin, Erro = null });
            _token.Obtido += tokens =>
            {
                if (Situacao.Estado != Estado.Conectado || Situacao.Email != tokens.Email)
                    conector.Registrar(ambiente, $"conectado como {tokens.Email}: localhost:{ambiente.PortaLocal} pronto para o DBeaver");
                Atualizar(s => s with { Estado = Estado.Conectado, Email = tokens.Email, SessaoAte = tokens.SessaoAte, Erro = null });
            };
            _sessao = new Sessao(ambiente, _token, conector._opcoes, conector.SenhaLocal, conector.Registrar);
        }

        public Situacao Situacao { get; private set; }

        public void Iniciar(bool entrarAgora)
        {
            _atendimento = Task.Run(AtenderAsync);
            // Login logo ao ligar: a primeira conexão do DBeaver já encontra o token.
            if (entrarAgora) _ = EntrarAsync();
        }

        public async Task EntrarAsync()
        {
            try
            {
                await _token.ObterAsync(_parar.Token);
            }
            catch (Exception e) when (!_parar.IsCancellationRequested)
            {
                _conector.Registrar(_ambiente, $"login não concluído ({e.Message}); tento de novo na próxima conexão do DBeaver");
                Atualizar(s => s with { Estado = Estado.Falhou, Erro = e.Message });
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task AtenderAsync()
        {
            try
            {
                while (!_parar.IsCancellationRequested)
                {
                    var cliente = await _escuta.AcceptTcpClientAsync(_parar.Token);
                    _ = Task.Run(async () =>
                    {
                        Atualizar(s => s with { Conexoes = Interlocked.Increment(ref _conexoes) });
                        try
                        {
                            await _sessao.AtenderAsync(cliente, _parar.Token);
                        }
                        catch (Exception e) when (e is IOException or SocketException or System.Security.Authentication.AuthenticationException or InvalidDataException)
                        {
                            _conector.Registrar(_ambiente, $"conexão encerrada: {e.Message}");
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        finally
                        {
                            Atualizar(s => s with { Conexoes = Interlocked.Decrement(ref _conexoes) });
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Atualizar(Func<Situacao, Situacao> mudanca)
        {
            Situacao nova;
            lock (_trava)
            {
                if (_parar.IsCancellationRequested) return;
                Situacao = nova = mudanca(Situacao);
            }
            _conector.Mudou?.Invoke(_ambiente, nova);
        }

        public async ValueTask DisposeAsync()
        {
            lock (_trava) _parar.Cancel();
            _escuta.Stop();
            try { await _atendimento; } catch { /* já parou */ }
        }
    }
}
