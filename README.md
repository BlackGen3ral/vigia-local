# Vigia Local

Monitoramento de entrada para lojas, separado do Artho AI. O computador Windows recebe vídeo RTSP de um gravador na mesma rede, acompanha pessoas com identificadores temporários e conta o cruzamento de uma linha no sentido de entrada, sem reconhecimento facial. O [site e app instalável](https://vigia-local.pages.dev/) recebem eventos. Em **Equipe e avisos**, é possível optar por anexar uma miniatura JPEG de até 24 KB. O link da imagem expira em 10 minutos. O vídeo ao vivo e a senha da câmera ficam no PC.

## Como usar

1. Instale o [Vigia Local para Windows](https://github.com/BlackGen3ral/vigia-local/releases/latest).
2. Informe IP local, porta, canal e credenciais do gravador. Ao conectar, mantenha a entrada vazia até a calibração terminar. Arraste uma linha sobre o piso da porta. A seta indica a entrada: use **Inverter direção de entrada** para apontá-la para dentro da loja. A área antiga é preservada, mas é preciso desenhar a linha para ativar a nova contagem.
3. Vincule a loja com a chave de instalação entregue pelo administrador do serviço. Use **Copiar código de convite** para autorizar quem deve receber avisos. O código de 12 caracteres permite vincular novos aparelhos por 7 dias. Compartilhe apenas com a equipe.
4. Abra o painel em vigia-local.pages.dev e informe o código. No Android, [baixe o APK assinado](https://github.com/BlackGen3ral/vigia-local/releases/latest/download/VigiaLocal-Android.apk), abra o Vigia Local e digite o código. No iPhone, use **Adicionar à Tela de Início** no Safari. Ative os avisos e use **Enviar aviso de teste** no Windows.
5. Fechar a janela deixa o Vigia na bandeja. O PC e o gravador precisam ficar ligados. Você pode habilitar a inicialização com o Windows.

A linha, a área anterior e as credenciais são protegidas pela conta do Windows neste PC. O painel remoto exibe os eventos recentes. Cada pessoa acompanhada que cruza a linha de fora para dentro gera um registro, inclusive em grupos e em intervalos menores que 20 segundos. O intervalo de 20 segundos controla apenas os avisos sonoros e remotos; o histórico e o total continuam contando. O Android e o painel mostram também entradas sem novo aviso. O total começa com a migração 005, sem reconstruir eventos antigos descartados; os registros anteriores permanecem disponíveis. O acompanhamento é geométrico, tolera breves falhas e não usa reconhecimento facial. Oclusões, cruzamentos entre pessoas e reconexões podem afetar a precisão; pessoas já dentro da loja ao iniciar não geram entrada. A precisão depende da posição da câmera, do tamanho das pessoas e da iluminação.

## Android nativo

O aplicativo é instalado por APK no Android 8 ou mais recente. Ative os avisos no app e permita as notificações. Um serviço visível na barra de notificações consulta as chegadas em segundo plano a cada 25 segundos quando o Android permite acesso à rede. Se o sistema encerrar o serviço ou o aparelho reiniciar, reabra o aplicativo. O som segue o canal Chegadas e testes.

## Publicação

Uma tag estável no formato `v1.2.3` cria um instalador Windows, um APK Android assinado e seus manifestos SHA-256 no GitHub. O Android avisa quando há versão nova. A instalação do APK exige a confirmação do sistema. O Windows procura novas versões ao abrir e a cada 30 minutos, baixa o instalador verificado e aplica quando a câmera está desconectada. Se ela estiver conectada, escolha **Instalar atualização** em Configurações para pausar o monitoramento e atualizar. A publicação usa `CLOUDFLARE_API_TOKEN` para o Worker e `CLOUDFLARE_PAGES_API_TOKEN` para o site no Pages. O painel antigo permanece acessível para os aparelhos já vinculados.

Para compilar localmente, use `./scripts/build-windows.ps1` com .NET 10 e Inno Setup. O site usa `npm ci` e `npm run deploy`. O Worker usa D1 com o esquema em `schema.sql` e as migrações em `migrations/` e os segredos `ENROLL_KEY`, `VAPID_PUBLIC` e `VAPID_PRIVATE`.

O modelo YOLOX-S vem do [OpenCV Zoo](https://github.com/opencv/opencv_zoo/tree/main/models/object_detection_yolox) e sua licença acompanha o app.

## Migração da contagem 1.3.0

Antes de publicar o Worker 1.3.0, aplique apenas migrations/005-directional-counting.sql no banco existente pela conexão administrativa Cloudflare. A mudança adiciona campos e um gatilho, preservando eventos e vínculos. O token de publicação do GitHub continua limitado ao serviço e ao site. Opcionalmente, scripts/migrate-counting.mjs verifica e aplica essa migração com uma credencial D1 autorizada. Use a opção deploy_only do workflow para publicar o serviço e o site sem recompilar aplicativos. A migração 005 foi aplicada em produção em 09/11/2026; 36 eventos e o vínculo existente foram preservados na verificação.
