# DiscordProxyRelay

Launcher para Windows x64 que inicia o Discord Stable usando temporariamente
um proxy publico fora do Brasil. Depois que o gateway do Discord e observado,
o relay troca novas conexoes para a internet direta. Conexoes destinadas a
`discord.media` usam a rota direta desde o inicio.

O programa nao modifica arquivos do Discord, nao injeta codigo no cliente e nao
instala servicos no Windows.

## Download

Baixe `DiscordProxyRelay.exe` pela pagina de
[Releases](https://github.com/uJFalkez/DiscordProxyRelay/releases/latest).

O programa suporta somente:

- Windows x64;
- Discord Stable instalado no perfil do usuario;
- uma unica instancia do launcher por vez.

## Como usar

1. Feche completamente o Discord, inclusive o icone na bandeja.
2. Execute `DiscordProxyRelay.exe`.
3. Aguarde a busca e validacao de um proxy.
4. O launcher abre o Discord automaticamente.
5. Depois da conexao, o console e ocultado e o restante da sessao segue direto.

Para manter o console aberto e exibir tambem os logs do Discord:

```powershell
DiscordProxyRelay.exe --verbose
```

Sem `--verbose`, os logs internos do Discord sao descartados. As mensagens do
launcher e os proxies publicos testados continuam aparecendo no console. Nenhum
log e salvo em arquivo.

## Aviso do Windows SmartScreen

O DiscordProxyRelay nao possui assinatura digital, entao o Windows pode exibir
um aviso ao executa-lo. Isso e esperado: o programa funciona normalmente, nao
coleta dados pessoais e e open source. Voce pode ler o codigo ou compilar o
executavel por conta propria.

Se baixou pela pagina oficial de Releases, clique em **Mais informacoes** e
depois em **Executar assim mesmo**.

Cada Release inclui `SHA256SUMS.txt`. Para verificar o arquivo baixado:

```powershell
Get-FileHash .\DiscordProxyRelay.exe -Algorithm SHA256
```

Compare o resultado com o hash publicado na mesma Release. Nao desative o
Microsoft Defender e nao adicione exclusoes permanentes.

## Como funciona

1. Consulta a API publica da [ProxyScrape](https://proxyscrape.com/).
2. Descarta proxies brasileiros, inativos, sem suporte TLS ou com metricas
   invalidas.
3. Ordena por disponibilidade e latencia.
4. Testa ate 12 proxies SOCKS5; se nenhum funcionar, testa ate 12 HTTP CONNECT.
5. Exige um tunel com certificado TLS valido para `gateway.discord.gg`.
6. Sobe um relay HTTP CONNECT somente em `127.0.0.1` e inicia o Discord com esse
   relay como proxy.
7. `discord.media` e seus subdominios ignoram o proxy, inclusive nas portas RTC
   alternativas.
8. Dez segundos depois de observar o gateway, fecha os tuneis de bootstrap e
   passa novas conexoes para a rota direta.

O relay nao descriptografa TLS, nao instala certificados e nao le o conteudo das
conexoes do Discord.

## Privacidade e riscos

- O programa nao le nem salva tokens, mensagens, contas, cookies ou payloads do
  Discord.
- O endereco de cada proxy testado e exibido no console, mas nao e salvo.
- A consulta ao catalogo da ProxyScrape envia a essa API seu IP publico e os
  metadados normais de uma requisicao HTTPS.
- Um proxy publico ve seu IP de origem, os destinos acessados, horarios e volume
  de trafego. O conteudo HTTPS/WSS permanece protegido por TLS.
- Proxies publicos podem ficar lentos, desaparecer, rejeitar destinos ou causar
  verificacoes adicionais do Discord.
- Se a inicializacao falhar, feche o Discord e tente novamente para selecionar
  outro proxy.

## Limitacoes

- PTB, Canary, macOS, Linux e Discord no navegador nao sao suportados.
- A disponibilidade de recursos depende do comportamento atual do Discord e
  pode mudar sem aviso.
- Se o gateway reconectar pela rota direta, pode ser necessario reiniciar o
  launcher.
- Nao ha garantia de que toda conta, servidor ou sessao tera os mesmos recursos.

## Build

Requisitos:

- SDK do .NET 9 compativel com o runtime `9.0.19`;
- Bash;
- acesso ao NuGet durante a restauracao inicial.

Execute:

```bash
./scripts/build-proxy-relay.sh
```

O script roda os testes e publica:

```text
artifacts/proxy-relay/win-x64/DiscordProxyRelay.exe
artifacts/proxy-relay/win-x64/SHA256SUMS.txt
```

O executavel e self-contained; o computador de destino nao precisa instalar o
.NET Runtime. A Release tambem inclui os avisos de licenca dos componentes do
.NET redistribuidos no executavel.

## Aviso legal

Este e um projeto independente, sem afiliacao com Discord ou ProxyScrape. O uso
de proxies e o contorno de restricoes regionais podem contrariar os Termos de
Servico do Discord. Use por sua conta e risco.

## Licenca

[MIT](LICENSE)
