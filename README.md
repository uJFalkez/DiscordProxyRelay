# DiscordProxyRelay

[![Última versão](https://img.shields.io/github/v/release/uJFalkez/DiscordProxyRelay?display_name=tag&style=flat-square)](https://github.com/uJFalkez/DiscordProxyRelay/releases/latest)
[![Plataforma](https://img.shields.io/badge/plataforma-Windows%20x64-0078D4?style=flat-square&logo=windows)](#requisitos)
[![.NET](https://img.shields.io/badge/.NET-9.0.19-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Licença](https://img.shields.io/github/license/uJFalkez/DiscordProxyRelay?style=flat-square)](LICENSE)

Qualquer dúvida, me chame no meu discord: `falkezz#5262`

> [!IMPORTANT]
> O programa usa proxies públicos de terceiros — leia [Privacidade e riscos](#privacidade-e-riscos) antes de usar. Baixe apenas da página oficial de [Releases](https://github.com/uJFalkez/DiscordProxyRelay/releases/latest).
>
> O Relay é necessário para TODOS que querem assistir ou streamar, não só para o streamer.

## Instalação

O programa é portátil: sem instalador e sem precisar instalar o .NET Runtime.

### Requisitos

- Windows x64;
- Discord Stable.

Discord PTB, Canary, navegador, macOS e Linux não são compatíveis. O Vencord é compatível, mas plugins que mexem na rede podem interferir.

### 1. Baixe

1. Acesse a [versão mais recente](https://github.com/uJFalkez/DiscordProxyRelay/releases/latest) e expanda **Assets**.
2. Baixe `DiscordProxyRelay.exe` (e, opcionalmente, `SHA256SUMS.txt`).
3. Coloque os arquivos na mesma pasta.

Não baixe de sites ou repositórios não oficiais.

### 2. Verifique (opcional)

No PowerShell, na pasta dos arquivos:

```powershell
$expected = (Get-Content .\SHA256SUMS.txt).Split()[0]
$actual = (Get-FileHash .\DiscordProxyRelay.exe -Algorithm SHA256).Hash.ToLower()
$actual -eq $expected
```

O resultado deve ser `True`.

### 3. Feche o Discord

Feche o Discord completamente (Alt+F4 e, se preciso, **Sair do Discord** no system tray).

### 4. Execute

Abra `DiscordProxyRelay.exe` e aguarde: o programa busca e testa proxies, seleciona uma conexão e abre o Discord sozinho. Não abra o Discord manualmente durante essa etapa.

> **SmartScreen:** o executável não é assinado, então o Windows pode mostrar "O Windows protegeu o computador". Baixou da página oficial e conferiu o checksum? Clique em **Mais informações** → **Executar assim mesmo**. Não é preciso desativar o Defender.

## Como usar

Basta fechar o Discord e executar `DiscordProxyRelay.exe`. O console mostra o progresso, mas nada é salvo em arquivo. Se falhar, feche o Discord e tente de novo.

### Argumentos

| Argumento | Padrão | Descrição |
| --- | --- | --- |
| `--verbose` | Desativado | Mantém o console aberto e mostra os logs internos do Discord. |
| [Experimental] `--persist-gateway` | Desativado | Mantém só o gateway do Discord no proxy após a inicialização. Booleano: basta estar presente. |

## Como funciona

- Busca proxies públicos da [ProxyScrape](https://proxyscrape.com/) de países aprovados (prioridade `US`/`CA`, depois uma lista secundária).
- Valida um túnel TLS para `gateway.discord.gg` e abre o Discord por um relay local em `127.0.0.1`.
- Conexões de voz e vídeo (`discord.media`) nunca passam pelo proxy.
- Dez segundos após identificar o gateway, o tráfego comum passa a usar a rede direta.
- Com `--persist-gateway`, o gateway fica no proxy e troca de proxy após duas falhas consecutivas ao conectar; sem substituto, mantém o atual e nunca usa conexão direta.

O relay não descriptografa TLS nem instala certificados. A filtragem geográfica não torna proxies públicos confiáveis.

## Problemas conhecidos

| Origem | Bug | Status | Correção |
| --- | --- | --- | --- |
| [@light1ngbolt no X](https://x.com/light1ngbolt/status/2089861213387665767) | A feature fica indisponível como se tivesse iniciado no Brasil. | Externo ao relay | Causado pelo plugin `BetterSessions` do Vencord. |
| Geral | Depois de um tempo, não conecta a uma live de novo. | Em teste | Use `--persist-gateway` para manter as reconexões no proxy. |
| Casos isolados | Console abre no app Terminal e não fecha sozinho. | Baixa prioridade | Sem correção ainda. |

O comportamento depende do Discord atual e pode mudar sem aviso.

## Privacidade e riscos

- O programa não lê nem salva tokens, mensagens, contas, cookies ou conteúdo do Discord.
- Proxies públicos podem ver seu IP de origem, destinos, horários e volume — mas o conteúdo HTTPS/WSS fica protegido por TLS.
- Proxies públicos podem ficar lentos, sumir ou provocar verificações do Discord.
- Não compartilhe capturas do console sem revisar os endereços de proxy exibidos.

## Compilação

Requisitos: SDK do .NET 9 (runtime `9.0.19`), Bash e acesso ao NuGet.

```bash
./scripts/build-proxy-relay.sh
```

Gera `artifacts/proxy-relay/win-x64/DiscordProxyRelay.exe` e `SHA256SUMS.txt` (executável self-contained).

## Aviso legal

Projeto independente, sem afiliação com Discord ou ProxyScrape. Usar proxies e contornar restrições regionais pode violar os Termos de Serviço do Discord. Use por sua conta e risco.

## Licença

Distribuído sob a [licença MIT](LICENSE).
