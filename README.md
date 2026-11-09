# Vigia Local

Monitoramento de entrada para lojas, separado do Artho AI. O computador Windows recebe vídeo RTSP de um gravador na mesma rede, detecta pessoas em movimento e verifica a área da porta. O [site e app instalável](https://vigia-local.pages.dev/) recebem apenas eventos, sem vídeo, senha da câmera ou imagem.

## Como usar

1. Instale o [Vigia Local para Windows](https://github.com/BlackGen3ral/vigia-local/releases/latest).
2. Informe IP local, porta, canal e credenciais do gravador. Ao conectar, mantenha a entrada vazia até a calibração terminar. Arraste um retângulo sobre a área da porta.
3. Vincule a loja com a chave de instalação entregue pelo administrador do serviço. Use **Copiar código de convite** para autorizar quem deve receber avisos. O código de 12 caracteres permite vincular novos aparelhos por 7 dias. Compartilhe apenas com a equipe.
4. Abra o painel em vigia-local.pages.dev e informe o código. No Android, use a opção de instalação do Chrome. No iPhone, use **Adicionar à Tela de Início** no Safari e abra o ícone instalado. Ative os avisos e use **Enviar aviso de teste** no Windows.
5. Fechar a janela deixa o Vigia na bandeja. O PC e o gravador precisam ficar ligados. Você pode habilitar a inicialização com o Windows.

A zona e as credenciais são protegidas pela conta do Windows neste PC. O painel remoto exibe os eventos recentes. A precisão depende da posição da câmera, do tamanho das pessoas e da iluminação.

## Publicação

Uma tag estável no formato `v1.2.3` cria um instalador Windows e um manifesto SHA-256 no GitHub. O Windows procura novas versões ao abrir e a cada 30 minutos, baixa o instalador verificado e aplica quando a câmera está desconectada. Se ela estiver conectada, escolha **Instalar atualização** em Configurações para pausar o monitoramento e atualizar. A publicação usa `CLOUDFLARE_API_TOKEN` para o Worker e `CLOUDFLARE_PAGES_API_TOKEN` para o site no Pages. O painel antigo permanece acessível para os aparelhos já vinculados.

Para compilar localmente, use `./scripts/build-windows.ps1` com .NET 10 e Inno Setup. O site usa `npm ci` e `npm run deploy`. O Worker usa D1 com o esquema em `schema.sql` e as migrações em `migrations/` e os segredos `ENROLL_KEY`, `VAPID_PUBLIC` e `VAPID_PRIVATE`.

O modelo YOLOX-S vem do [OpenCV Zoo](https://github.com/opencv/opencv_zoo/tree/main/models/object_detection_yolox) e sua licença acompanha o app.
