<p align="center">
  <img src="src/ScreenRecorder/Assets/App.png" width="88" alt="Ícone do Gravador de Tela">
</p>

# Gravador de Tela

Gravador de tela portátil para **Windows**, desenvolvido em **C# / .NET 10 e WPF**. Interface compacta, controle pela bandeja e gravação de tela inteira ou de uma área selecionada.

## Funcionalidades

- Seleção de monitor, tela inteira ou área retangular.
- Botão único para **iniciar, pausar e retomar**; botão separado para finalizar e salvar.
- Minimização automática na bandeja ao iniciar a gravação.
- Menu na bandeja para controlar a gravação, mostrar a janela, abrir a pasta e sair.
- MP4 e MKV, com H.264 ou H.265 e opções de 30 ou 60 quadros por segundo.
- Áudio do PC e do microfone com opções independentes.
- Cursor opcional e realce amarelo semitransparente que acompanha o ponteiro.
- Atalhos globais personalizáveis, com detecção de combinações duplicadas ou em uso.
- Pasta de destino configurável e preferências salvas entre execuções.
- Executável autocontido, sem instalador.

## Como usar

Depois de gerar a publicação, abra `artifacts/portable/win-x64/GravadorDeTela.exe`.

1. Escolha o monitor na lista.
2. Use o ícone de **monitor** para tela inteira ou o ícone de **seleção** para demarcar uma área. Arraste o retângulo; `Esc` cancela a seleção.
3. Abra a **engrenagem** para configurar vídeo, áudio, mouse, pasta e atalhos. Clique em **Salvar** para aplicar ou em **Cancelar** para descartar.
4. Clique no **círculo vermelho** para iniciar. A janela será recolhida na bandeja, junto ao relógio; o ícone pode ficar na seta de ícones ocultos.
5. Use o menu da bandeja, os atalhos ou restaure a janela para pausar e retomar.
6. Clique no **quadrado azul** para finalizar. Aguarde o salvamento; a janela será restaurada.

O botão principal exibe círculo vermelho para iniciar, barras amarelas para pausar e triângulo verde para retomar. Os controles têm dicas ao passar o mouse e nomes para tecnologias assistivas.

O ícone de **pasta** abre o local das gravações. Ao finalizar, a seleção é cancelada automaticamente e o modo volta para tela inteira.

### Área selecionada

A borda azul indica a área demarcada; durante a gravação fica vermelha e, na pausa, laranja. Ela não intercepta cliques. No Windows 10 2004 ou posterior, a borda é excluída da captura.

### Atalhos padrão

| Ação | Atalho |
| --- | --- |
| Iniciar | `Ctrl+Shift+F8` |
| Pausar ou retomar | `Ctrl+Shift+F9` |
| Finalizar e salvar | `Ctrl+Shift+F10` |

Para alterar, clique no campo correspondente nas configurações e pressione a combinação desejada. `Backspace` ou `Delete` desativa o atalho. Os atalhos ficam suspensos durante a edição das configurações.

## Compatibilidade

- **Windows 10 1903 ou posterior / Windows 11, x64**.
- O executável publicado inclui o runtime .NET, o FFmpeg e as dependências C++ locais. Esses componentes são extraídos automaticamente na primeira execução; não precisam ser instalados separadamente pelo usuário.
- H.265 depende de um codificador HEVC disponível no Windows ou no hardware. H.264 é o padrão.
- Aceleração por hardware depende do driver e dos codificadores disponíveis.
- O áudio usa os dispositivos padrão do Windows. PC e microfone são misturados em uma trilha AAC quando ambos estão ativos.
- O acesso ao microfone depende das permissões do Windows e de programas de segurança instalados no computador.
- Windows N/KN precisa dos recursos de mídia correspondentes. Conteúdo protegido e telas seguras podem não ser capturados.
- A área fica dentro do monitor escolhido; as dimensões do vídeo são ajustadas a pixels pares.

O MKV é gerado a partir da gravação MP4 por remux, sem recodificar. Se essa conversão falhar, o MP4 intermediário é preservado na pasta escolhida.

O realce do mouse aparece também na tela durante a gravação, desaparece na pausa ou fora da área capturada e é removido ao finalizar. É possível gravar apenas o realce, sem a imagem do cursor.

## Compilar e executar

### Pré-requisitos de desenvolvimento

- Windows x64.
- .NET SDK **10.0.200** ou versão compatível com `global.json`.
- Visual Studio com as ferramentas C++ e os componentes redistribuíveis MSVC x64 para preparar o pacote portátil.
- Opcional: VS Code com as extensões C# e C# Dev Kit.

```powershell
git clone https://github.com/Joao-Dornelas/gravador-de-tela.git
cd gravador-de-tela

.\scripts\setup-tools.ps1
dotnet restore --locked-mode
dotnet build -c Release
dotnet run --project src/ScreenRecorder/ScreenRecorder.csproj
```

O script de preparação baixa o FFmpeg de uma versão registrada, valida o SHA256 e copia o runtime redistribuível C++ da instalação local do Visual Studio. As ferramentas baixadas e os artefatos gerados ficam fora do Git.

### Gerar o executável portátil

```powershell
.\scripts\publish.ps1
```

Saída:

```text
artifacts/portable/win-x64/GravadorDeTela.exe
```

O perfil `Portable` gera um único executável autocontido. O FFprobe é usado apenas no ambiente de desenvolvimento para validar os vídeos.

## Testes

Os testes são executados em uma sessão Windows desbloqueada. Eles produzem gravações e relatórios locais em `artifacts/`.

```powershell
# Bandeja, botão unificado, borda, limpeza da seleção, atalhos e conflitos.
# Não acessa áudio nem microfone.
dotnet run --project tests/ScreenRecorder.SmokeTests/ScreenRecorder.SmokeTests.csproj -c Release -- artifacts/desktop-controls --desktop-controls

# Quatro combinações de cursor e realce, verificadas nos pixels dos vídeos.
# Mantenha o ponteiro parado durante este teste.
dotnet run --project tests/ScreenRecorder.SmokeTests/ScreenRecorder.SmokeTests.csproj -c Release -- artifacts/mouse-tests --mouse-only

# Captura, pausa, codecs, contêineres e áudio.
# Este conjunto acessa áudio do PC e microfone e reproduz um tom curto.
dotnet run --project tests/ScreenRecorder.SmokeTests/ScreenRecorder.SmokeTests.csproj -c Release -- artifacts/smoke-tests
```

Os testes de gravação verificam dimensões, codec, trilhas de áudio, duração com a pausa removida e decodificação do arquivo. Cada conjunto salva um `report.json`. Ausência de microfone ou de HEVC é registrada separadamente pelo conjunto completo.

## Estrutura

| Caminho | Conteúdo |
| --- | --- |
| `src/ScreenRecorder/` | Aplicativo WPF, configurações e sobreposições de seleção e cursor |
| `src/ScreenRecorder/Services/` | Captura, bandeja, atalhos, preferências e processamento de mídia |
| `src/ScreenRecorder/Assets/` | Ícone compartilhado da janela, executável e bandeja |
| `tests/ScreenRecorder.SmokeTests/` | Testes de integração com gravações locais |
| `scripts/` | Preparação das ferramentas, publicação e geração do ícone |
| `docs/` | Procedência do FFmpeg e licença da biblioteca de captura |

As preferências ficam em `%LOCALAPPDATA%/ScreenRecorder/settings.json`. Os arquivos `errors.log`, `session.log` e `recorder-native.log` na mesma pasta auxiliam o diagnóstico. Esse caminho é mantido para preservar as configurações das versões iniciais.

## Componentes de terceiros

- [ScreenRecorderLib 7.0.1](https://github.com/sskodje/ScreenRecorderLib): Windows Graphics Capture, Media Foundation e áudio WASAPI. Licença MIT disponível em [docs/ScreenRecorderLib-LICENSE.txt](docs/ScreenRecorderLib-LICENSE.txt).
- [FFmpeg](https://ffmpeg.org/), build LGPL de [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds): remux para MKV. A origem e o SHA256 estão em [docs/ffmpeg-source.json](docs/ffmpeg-source.json); a licença do pacote é incorporada à publicação.
- Runtime redistribuível Microsoft Visual C++, copiado da instalação local do Visual Studio na preparação das ferramentas.

O ícone contém resoluções de 16 a 256 pixels e pode ser regenerado com `scripts/generate-icon.ps1`.
