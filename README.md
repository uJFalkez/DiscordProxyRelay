# DiscordProxyRelay

[![Última versão](https://img.shields.io/github/v/release/uJFalkez/DiscordProxyRelay?display_name=tag&style=flat-square)](https://github.com/uJFalkez/DiscordProxyRelay/releases/latest)
[![Plataforma](https://img.shields.io/badge/plataforma-Windows%20x64-0078D4?style=flat-square&logo=windows)](#compatibilidade)
[![.NET](https://img.shields.io/badge/.NET-9.0.19-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Licença](https://img.shields.io/github/license/uJFalkez/DiscordProxyRelay?style=flat-square)](LICENSE)

> [!IMPORTANT]
> O projeto utiliza proxies públicos de terceiros. Leia a seção [Privacidade e riscos](#privacidade-e-riscos) antes de usar.
>
> O Relay é necessário para TODOS que querem tanto assistir quanto streamar, não basta só o streamer ter instalado!!
>
> O Windows SmartScreen não gosta muito de executáveis desconhecidos, portanto, só execute o programa se for baixado estritamente [desta página](https://github.com/uJFalkez/DiscordProxyRelay/releases/latest). Se não confia mesmo assim, sinta-se livre para buildar o programa em sua máquina e verificar o checksum. Além disso tudo: é open-source, basta ler o código se quiser.

## Instalação

O DiscordProxyRelay é portátil: não possui instalador e não exige a instalação separada do .NET Runtime.

### Requisitos

- Windows x64;
- Discord (stable release);

Discord PTB, Canary, navegador, macOS e Linux não são compatíveis.

### 1. Baixe o programa

1. Acesse a página da [versão mais recente](https://github.com/uJFalkez/DiscordProxyRelay/releases/latest).
2. Expanda a seção **Assets**, caso ela esteja recolhida.
3. Baixe o arquivo `DiscordProxyRelay.exe`.
4. Para verificar a integridade do download, baixe também o arquivo `SHA256SUMS.txt`.
5. Coloque os dois arquivos na mesma pasta. Você pode usar qualquer pasta comum, como `Downloads` ou `Documentos`.

Não baixe o executável de sites, encurtadores ou repositórios não oficiais.

### 2. Verifique o arquivo (OPCIONAL)

Abra o PowerShell na pasta em que os arquivos foram salvos e execute:

```powershell
$expected = (Get-Content .\SHA256SUMS.txt).Split()[0]
$actual = (Get-FileHash .\DiscordProxyRelay.exe -Algorithm SHA256).Hash.ToLower()
$actual -eq $expected
```

O resultado deve ser `True`. Se aparecer `False`, apague o executável e faça o download novamente pela página oficial de Releases.

### 3. Feche o Discord

Feche completamente o Discord antes de iniciar o launcher usando Alt+F4. Se o ícone ainda aparecer no system tray, clique nele com o botão direito e selecione **Sair do Discord**.

### 4. Execute o launcher

Abra `DiscordProxyRelay.exe` e aguarde. O programa irá:

1. buscar proxies disponíveis;
2. testar os proxies encontrados;
3. selecionar uma conexão válida;
4. abrir o Discord automaticamente;
5. ocultar o console depois que a conexão inicial estiver concluída.

O processo pode demorar enquanto os proxies públicos são testados. Não abra o Discord manualmente durante essa etapa.

### Aviso do Windows SmartScreen

O executável não possui assinatura digital. Por isso, o Windows SmartScreen pode exibir a mensagem **O Windows protegeu o computador** na primeira execução.

Se você baixou o arquivo pela página oficial de Releases e conferiu o checksum:

1. clique em **Mais informações**;
2. confirme que o aplicativo exibido é `DiscordProxyRelay.exe`;
3. clique em **Executar assim mesmo**.

Esse aviso não significa que seja necessário desativar o Microsoft Defender. Não crie exclusões permanentes para o programa.

## Como usar

Na utilização normal, basta fechar o Discord e executar `DiscordProxyRelay.exe`. O console mostra o andamento da conexão e os proxies testados, mas nenhum log é salvo em arquivo.

Se a inicialização falhar, feche completamente o Discord e execute o launcher novamente. Uma nova tentativa poderá selecionar outro proxy.

## Visão geral

O Discord é iniciado temporariamente por um proxy público de um dos países aprovados, sem modificar arquivos do cliente, injetar código ou instalar serviços no Windows.
Depois que o gateway do Discord é identificado, novas conexões passam a usar rede brasileira diretamente. Conexões de voz e vídeo destinadas a `discord.media` nem tocam o proxy.

### Verbose

Para manter o console aberto e exibir também os logs do Discord, execute pelo PowerShell:

```powershell
.\DiscordProxyRelay.exe --verbose
```

Sem `--verbose`, os logs internos do Discord são descartados após a leitura. As mensagens do launcher continuam visíveis durante a inicialização.

## Compatibilidade

| Sistema operacional | Windows x64 |
| Cliente | Discord Stable |
| Discord PTB e Canary | Não |
| Discord no navegador | Não |
| Runtime adicional | Não é necessário |
| Instalação no sistema | Não é necessária |

## Como funciona

1. Consulta a API pública da [ProxyScrape](https://proxyscrape.com/).
2. Aceita somente proxies dos países preferenciais `US` e `CA` ou dos países secundários `GB`, `IE`, `DE`, `FR`, `NL`, `BE`, `LU`, `CH`, `AT`, `DK`, `NO`, `SE`, `FI`, `IS`, `AU`, `NZ`, `JP` e `SG`.
3. Descarta proxies inativos, sem suporte a TLS ou com métricas inválidas.
4. Ordena os resultados por disponibilidade e latência dentro de cada etapa.
5. Testa os proxies em quatro etapas estritas: SOCKS5 preferenciais, HTTP CONNECT preferenciais, SOCKS5 secundários e HTTP CONNECT secundários. Cada etapa testa no máximo seis proxies, totalizando no máximo 24 tentativas, e a etapa seguinte só começa se nenhuma conexão da anterior funcionar.
6. Não tenta proxies de países fora das listas aprovadas caso todas as quatro etapas falhem.
7. Exige um túnel com certificado TLS válido para `gateway.discord.gg`.
8. Inicia um relay HTTP CONNECT somente em `127.0.0.1` e abre o Discord por esse relay.
9. Faz com que `discord.media` e seus subdomínios ignorem o proxy, inclusive em portas RTC alternativas.
10. Dez segundos depois de identificar o gateway, encerra os túneis de inicialização e direciona novas conexões para a rota direta.

O relay não descriptografa TLS, não instala certificados e não lê o conteúdo das conexões do Discord.
A filtragem geográfica apenas limita os países usados e não torna proxies públicos confiáveis.

## Privacidade e riscos

- O programa não lê nem salva tokens, mensagens, contas, cookies ou conteúdo enviado pelo Discord.
- O endereço de cada proxy testado aparece no console, mas não é salvo.
- A consulta ao catálogo da ProxyScrape envia à API o seu endereço IP público e os metadados normais de uma requisição HTTPS.
- Um proxy público pode identificar seu IP de origem, os destinos acessados, os horários e o volume de tráfego. O conteúdo HTTPS e WSS permanece protegido por TLS.
- Proxies públicos podem ficar lentos, desaparecer, rejeitar destinos ou provocar verificações adicionais do Discord.
- Não compartilhe capturas do console sem revisar os endereços de proxy exibidos.

## Limitações

- A disponibilidade de recursos depende do comportamento atual do Discord e pode mudar sem aviso.
- Se o gateway se reconectar pela rota direta, pode ser necessário fechar o Discord e executar o launcher novamente.
- Não há garantia de que todas as contas, servidores ou sessões terão os mesmos recursos.
- O funcionamento final deve ser avaliado pelo próprio usuário, especialmente após atualizações do Discord.

## Compilação

### Requisitos

- SDK do .NET 9 compatível com o runtime `9.0.19`;
- Bash;
- acesso ao NuGet durante a restauração inicial.

Na raiz do repositório, execute:

```bash
./scripts/build-proxy-relay.sh
```

O script executa os testes e gera:

```text
artifacts/proxy-relay/win-x64/DiscordProxyRelay.exe
artifacts/proxy-relay/win-x64/SHA256SUMS.txt
```

O executável é self-contained; o computador de destino não precisa instalar o .NET Runtime. Cada Release também inclui os avisos de licença dos componentes do .NET redistribuídos no executável.

## Aviso legal

Este é um projeto independente, sem afiliação com o Discord ou a ProxyScrape. O uso de proxies e o contorno de restrições regionais podem contrariar os Termos de Serviço do Discord. Use por sua conta e risco.

## Licença

Distribuído sob a [licença MIT](LICENSE).
