# DiscordProxyRelay

[![Última versão](https://img.shields.io/github/v/release/uJFalkez/DiscordProxyRelay?display_name=tag&style=flat-square)](https://github.com/uJFalkez/DiscordProxyRelay/releases/latest)
[![Plataforma](https://img.shields.io/badge/plataforma-Windows%20x64-0078D4?style=flat-square&logo=windows)](#compatibilidade)
[![.NET](https://img.shields.io/badge/.NET-9.0.19-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Licença](https://img.shields.io/github/license/uJFalkez/DiscordProxyRelay?style=flat-square)](LICENSE)

Inicie o Discord Stable temporariamente por um proxy público localizado fora do Brasil, sem modificar arquivos do cliente, injetar código ou instalar serviços no Windows.

O DiscordProxyRelay usa o proxy apenas durante a inicialização. Depois que o gateway do Discord é identificado, novas conexões passam a usar a internet diretamente. Conexões de voz e vídeo destinadas a `discord.media` usam a rota direta desde o início.

> [!IMPORTANT]
> O projeto utiliza proxies públicos de terceiros. Leia a seção [Privacidade e riscos](#privacidade-e-riscos) antes de usar.

## Instalação

O DiscordProxyRelay é portátil: não possui instalador e não exige a instalação separada do .NET Runtime.

### Requisitos

- Windows x64;
- Discord Stable instalado no perfil do usuário;
- acesso à internet;
- nenhuma outra instância do DiscordProxyRelay em execução.

Discord PTB, Canary, navegador, macOS e Linux não são compatíveis.

### 1. Baixe o programa

1. Acesse a página da [versão mais recente](https://github.com/uJFalkez/DiscordProxyRelay/releases/latest).
2. Expanda a seção **Assets**, caso ela esteja recolhida.
3. Baixe o arquivo `DiscordProxyRelay.exe`.
4. Para verificar a integridade do download, baixe também o arquivo `SHA256SUMS.txt`.
5. Coloque os dois arquivos na mesma pasta. Você pode usar qualquer pasta comum, como `Downloads` ou `Documentos`.

Não baixe o executável de sites, encurtadores ou repositórios não oficiais.

### 2. Verifique o arquivo, se desejar

Abra o PowerShell na pasta em que os arquivos foram salvos e execute:

```powershell
$expected = (Get-Content .\SHA256SUMS.txt).Split()[0]
$actual = (Get-FileHash .\DiscordProxyRelay.exe -Algorithm SHA256).Hash.ToLower()
$actual -eq $expected
```

O resultado deve ser `True`. Se aparecer `False`, apague o executável e faça o download novamente pela página oficial de Releases.

### 3. Feche o Discord

Feche completamente o Discord antes de iniciar o launcher. Se o ícone ainda aparecer na bandeja do sistema, clique nele com o botão direito e selecione **Sair do Discord**.

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

### Modo detalhado

Para manter o console aberto e exibir também os logs do Discord, execute pelo PowerShell:

```powershell
.\DiscordProxyRelay.exe --verbose
```

Sem `--verbose`, os logs internos do Discord são descartados após a leitura. As mensagens do launcher continuam visíveis durante a inicialização.

## Compatibilidade

| Item | Suporte |
| --- | --- |
| Sistema operacional | Windows x64 |
| Cliente | Discord Stable |
| Discord PTB e Canary | Não |
| Discord no navegador | Não |
| Runtime adicional | Não é necessário |
| Instalação no sistema | Não é necessária |

## Como funciona

1. Consulta a API pública da [ProxyScrape](https://proxyscrape.com/).
2. Descarta proxies brasileiros, inativos, sem suporte a TLS ou com métricas inválidas.
3. Ordena os resultados por disponibilidade e latência.
4. Testa até 12 proxies SOCKS5. Se nenhum funcionar, testa até 12 proxies HTTP CONNECT.
5. Exige um túnel com certificado TLS válido para `gateway.discord.gg`.
6. Inicia um relay HTTP CONNECT somente em `127.0.0.1` e abre o Discord por esse relay.
7. Faz com que `discord.media` e seus subdomínios ignorem o proxy, inclusive em portas RTC alternativas.
8. Dez segundos depois de identificar o gateway, encerra os túneis de inicialização e direciona novas conexões para a rota direta.

O relay não descriptografa TLS, não instala certificados e não lê o conteúdo das conexões do Discord.

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
