# Vigia Local

Monitoramento de entrada para lojas, separado do Artho AI. O computador Windows recebe vídeo RTSP de um gravador na mesma rede, detecta pessoas em movimento e verifica a área da porta. O site e o app instalável recebem apenas eventos, sem vídeo, senha da câmera ou imagem.

## Como usar

1. Instale o [Vigia Local para Windows](https://github.com/BlackGen3ral/vigia-local/releases/latest).
2. Informe IP local, porta, canal e credenciais do gravador. Ao conectar, mantenha a entrada vazia até a calibração terminar. Arraste um retângulo sobre a área da porta.
3. Vincule a loja com a chave de instalação entregue pelo administrador do serviço. Use **Copiar convite da equipe** para autorizar quem deve receber avisos. O link dá acesso ao histórico e deve ser compartilhado apenas com a equipe.
4. Abra o convite no celular. No Android, use a opção de instalação do Chrome. No iPhone, use **Adicionar à Tela de Início** no Safari e abra o ícone instalado. Ative os avisos e use **Enviar aviso de teste** no Windows.
5. Fechar a janela deixa o Vigia na bandeja. O PC e o gravador precisam ficar ligados. Você pode habilitar a inicialização com o Windows.

A zona e as credenciais são protegidas pela conta do Windows neste PC. O painel remoto exibe os eventos recentes. A precisão depende da posição da câmera, do tamanho das pessoas e da iluminação.

## Publicação

Uma tag estável no formato `v1.2.3` cria um instalador Windows e um manifesto SHA-256 no GitHub. O Windows procura novas versões ao abrir e a cada 30 minutos, baixa o instalador verificado e aplica quando a câmera está desconectada ou ao usar **Encerrar Vigia Local**. O trabalho Cloudflare usa o segredo `CLOUDFLARE_API_TOKEN` do repositório para publicar o Worker e os arquivos do app instalável.

Para compilar localmente, use `./scripts/build-windows.ps1` com .NET 10 e Inno Setup. O site usa `npm ci` e `npm run deploy`. O Worker usa D1 com o esquema em `schema.sql` e os segredos `ENROLL_KEY`, `VAPID_PUBLIC` e `VAPID_PRIVATE`.

O modelo YOLOX-S vem do [OpenCV Zoo](https://github.com/opencv/opencv_zoo/tree/main/models/object_detection_yolox) e sua licença acompanha o app.
