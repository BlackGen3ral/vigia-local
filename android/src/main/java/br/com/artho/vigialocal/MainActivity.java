package br.com.artho.vigialocal;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.*;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.text.InputType;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.text.DateFormat;
import java.util.Date;
import java.util.concurrent.*;

public final class MainActivity extends Activity {
    private static final int BG = 0xff11141b, CARD = 0xff1c222d, FG = 0xfff0f3f7;
    private static final int MUTED = 0xffaab4c5, GREEN = 0xffa8ecc8;
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler main = new Handler(Looper.getMainLooper());
    private final Runnable autoRefresh = new Runnable() {
        @Override public void run() { refresh(); main.postDelayed(this, 20000); }
    };
    private SharedPreferences prefs;
    private LinearLayout content, history;
    private TextView status, alertStatus, feedback;
    private Button alertButton, update;
    private boolean fetching;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        prefs = getSharedPreferences(AlertService.PREFS, MODE_PRIVATE);
        AlertService.channels(this);
        render();
        checkUpdate();
    }

    @Override public void onResume() {
        super.onResume();
        if (alertButton != null) updateAlertState();
        if (enabled() && permissionGranted())
            startForegroundService(new Intent(this, AlertService.class));
        main.removeCallbacks(autoRefresh);
        main.post(autoRefresh);
    }
    @Override public void onPause() { main.removeCallbacks(autoRefresh); super.onPause(); }
    @Override public void onDestroy() { worker.shutdownNow(); super.onDestroy(); }

    private void render() {
        String token = prefs.getString(AlertService.TOKEN, null);
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(BG);
        content = column();
        content.setPadding(dp(21), dp(24), dp(21), dp(24));
        scroll.addView(content);
        scroll.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                content.setPadding(dp(21) + bars.left, dp(24) + bars.top, dp(21) + bars.right, dp(24) + bars.bottom);
            }
            return insets;
        });
        setContentView(scroll);
        alertButton = null;
        history = null;

        LinearLayout brand = new LinearLayout(this);
        brand.setGravity(Gravity.CENTER_VERTICAL);
        ImageView icon = new ImageView(this);
        icon.setImageResource(R.drawable.ic_vigia);
        brand.addView(icon, new LinearLayout.LayoutParams(dp(44), dp(44)));
        TextView name = text("Vigia Local", 19, FG, true);
        LinearLayout.LayoutParams nameParams = new LinearLayout.LayoutParams(-2, -2);
        nameParams.leftMargin = dp(12);
        brand.addView(name, nameParams);
        add(content, brand, 0);

        add(content, text(token == null ? "VINCULAR APARELHO" : "MONITORAMENTO DA LOJA", 11, GREEN, true), 28);
        add(content, text(token == null ? "Conecte sua loja" : "Entrada da loja", 32, FG, true), 6);
        add(content, text(token == null ? "Use o código de convite mostrado no Vigia Local do computador."
            : "Acompanhe chegadas e receba avisos neste celular.", 15, MUTED, false), 7);
        feedback = text("", 14, MUTED, false);

        if (token == null) {
            LinearLayout card = card();
            add(content, card, 20);
            add(card, text("Código de convite", 15, FG, true), 0);
            EditText input = new EditText(this);
            input.setSingleLine();
            input.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS);
            input.setTextColor(FG);
            input.setHintTextColor(MUTED);
            input.setHint("Ex.: 7K3M-9T6Q-2H4R");
            input.setPadding(dp(12), dp(9), dp(12), dp(9));
            input.setBackground(round(BG, 10));
            add(card, input, 9);
            Button link = button("Vincular aparelho", true);
            add(card, link, 13);
            link.setOnClickListener(view -> bind(input.getText().toString(), link));
        } else {
            LinearLayout live = card();
            add(content, live, 20);
            add(live, text("ESTADO DA LOJA", 11, GREEN, true), 0);
            status = text("Consultando computador da loja…", 17, FG, true);
            add(live, status, 7);
            LinearLayout alerts = card();
            add(content, alerts, 11);
            add(alerts, text("Notificações", 18, FG, true), 0);
            alertStatus = text("", 14, MUTED, false);
            add(alerts, alertStatus, 6);
            alertButton = button("Ativar avisos", true);
            add(alerts, alertButton, 12);
            alertButton.setOnClickListener(view -> toggleAlerts());
            Button system = button("Configurações de som", false);
            add(alerts, system, 8);
            system.setOnClickListener(view -> startActivity(new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
                .putExtra(Settings.EXTRA_APP_PACKAGE, getPackageName())));
            updateAlertState();

            LinearLayout events = card();
            add(content, events, 11);
            add(events, text("Chegadas recentes", 18, FG, true), 0);
            history = column();
            add(events, history, 4);
            add(history, text("Carregando histórico…", 14, MUTED, false), 9);
            Button unlink = button("Desvincular aparelho", false);
            add(content, unlink, 12);
            unlink.setOnClickListener(view -> new AlertDialog.Builder(this)
                .setTitle("Desvincular a loja?").setMessage("O celular deixa de receber os avisos. Você pode vinculá-lo novamente com um convite.")
                .setNegativeButton("Cancelar", null).setPositiveButton("Desvincular", (dialog, which) -> {
                    stopService(new Intent(this, AlertService.class));
                    prefs.edit().remove(AlertService.TOKEN).remove(AlertService.CURSOR).remove(AlertService.ENABLED).apply();
                    render();
                }).show());
        }
        add(content, feedback, 10);
        update = button("Baixar atualização", false);
        update.setVisibility(View.GONE);
        add(content, update, 10);
        update.setOnClickListener(view -> startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(VigiaApi.APK))));
        add(content, text("Versão " + version(), 12, MUTED, false), 12);
    }

    private void bind(String input, Button link) {
        link.setEnabled(false);
        feedback.setText("Vinculando…");
        worker.execute(() -> {
            try {
                String token = VigiaApi.pair(input);
                JSONObject result = VigiaApi.events(token);
                runOnUiThread(() -> {
                    if (isDestroyed()) return;
                    prefs.edit().putString(AlertService.TOKEN, token).apply();
                    render();
                    showEvents(result);
                });
            } catch (Exception e) {
                runOnUiThread(() -> {
                    if (isDestroyed()) return;
                    feedback.setText(e instanceof java.io.IOException && e.getMessage() != null &&
                        !e.getMessage().contains("https") ? e.getMessage() : "Não foi possível vincular. Confira o código e a internet.");
                    link.setEnabled(true);
                });
            }
        });
    }

    private void refresh() {
        String token = prefs.getString(AlertService.TOKEN, null);
        if (token == null || fetching) return;
        fetching = true;
        worker.execute(() -> {
            try {
                JSONObject result = VigiaApi.events(token);
                runOnUiThread(() -> {
                    if (!isDestroyed() && token.equals(prefs.getString(AlertService.TOKEN, null))) showEvents(result);
                });
            } catch (Exception ignored) {
                runOnUiThread(() -> {
                    if (!isDestroyed() && status != null) status.setText("Não foi possível consultar a loja.");
                });
            } finally { runOnUiThread(() -> fetching = false); }
        });
    }

    private void showEvents(JSONObject result) {
        if (history == null) return;
        status.setText(result.optBoolean("online") ? "Computador da loja online" : "Computador da loja offline");
        history.removeAllViews();
        JSONArray events = result.optJSONArray("events");
        if (events == null || events.length() == 0) {
            add(history, text("Nenhum aviso por enquanto.", 14, MUTED, false), 10);
            return;
        }
        for (int index = 0; index < Math.min(20, events.length()); index++) {
            JSONObject event = events.optJSONObject(index);
            if (event == null) continue;
            String title = "test".equals(event.optString("kind")) ? "Aviso de teste" : "Pessoa na entrada";
            String when = DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT).format(new Date(event.optLong("time")));
            add(history, text(title + "  ·  " + when, 14, FG, false), 10);
        }
    }

    private boolean enabled() { return prefs.getBoolean(AlertService.ENABLED, false); }
    private boolean permissionGranted() {
        return Build.VERSION.SDK_INT < 33 || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED;
    }

    private void updateAlertState() {
        boolean active = enabled() && permissionGranted();
        alertButton.setText(active ? "Pausar avisos" : "Ativar avisos");
        alertStatus.setText(active ? "Ativo. O Vigia continua buscando avisos em segundo plano." :
            "Ative para receber avisos mesmo quando a tela do Vigia estiver fechada.");
    }

    private void toggleAlerts() {
        if (enabled() && permissionGranted()) {
            prefs.edit().putBoolean(AlertService.ENABLED, false).apply();
            stopService(new Intent(this, AlertService.class));
            updateAlertState();
        } else if (!permissionGranted() && Build.VERSION.SDK_INT >= 33) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 10);
        } else { enableAlerts(); }
    }

    @Override public void onRequestPermissionsResult(int request, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(request, permissions, results);
        if (request != 10 || isDestroyed()) return;
        if (permissionGranted()) enableAlerts();
        else feedback.setText("Permita as notificações em Configurações de som para ativar os avisos.");
    }

    private void enableAlerts() {
        if (prefs.getString(AlertService.TOKEN, null) == null) return;
        prefs.edit().putBoolean(AlertService.ENABLED, true)
            .putLong(AlertService.CURSOR, System.currentTimeMillis()).apply();
        try {
            startForegroundService(new Intent(this, AlertService.class));
            feedback.setText("Para confirmar a entrega, use Enviar aviso de teste no Windows.");
        } catch (RuntimeException e) {
            prefs.edit().putBoolean(AlertService.ENABLED, false).apply();
            feedback.setText("O Android não conseguiu iniciar os avisos. Tente novamente com o app aberto.");
        }
        updateAlertState();
    }

    private void checkUpdate() {
        worker.execute(() -> {
            try {
                String tag = VigiaApi.latestVersion();
                if (newer(tag, version())) runOnUiThread(() -> { if (!isDestroyed()) update.setVisibility(View.VISIBLE); });
            } catch (Exception ignored) {}
        });
    }

    private static boolean newer(String tag, String current) {
        if (!tag.matches("v[0-9]+[.][0-9]+[.][0-9]+") || !current.matches("[0-9]+[.][0-9]+[.][0-9]+")) return false;
        String[] a = tag.substring(1).split("[.]"), b = current.split("[.]");
        for (int i = 0; i < 3; i++) {
            int comparison = Integer.compare(Integer.parseInt(a[i]), Integer.parseInt(b[i]));
            if (comparison != 0) return comparison > 0;
        }
        return false;
    }

    private String version() {
        try { return getPackageManager().getPackageInfo(getPackageName(), 0).versionName; }
        catch (PackageManager.NameNotFoundException e) { return ""; }
    }
    private int dp(int value) { return Math.round(getResources().getDisplayMetrics().density * value); }
    private LinearLayout column() { LinearLayout layout = new LinearLayout(this); layout.setOrientation(LinearLayout.VERTICAL); return layout; }
    private LinearLayout card() { LinearLayout layout = column(); layout.setPadding(dp(15), dp(15), dp(15), dp(15)); layout.setBackground(round(CARD, 14)); return layout; }
    private void add(LinearLayout parent, View child, int margin) { LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, -2); params.topMargin = dp(margin); parent.addView(child, params); }
    private TextView text(String value, int size, int color, boolean bold) {
        TextView view = new TextView(this); view.setText(value); view.setTextSize(size); view.setTextColor(color);
        if (bold) view.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        return view;
    }
    private Button button(String value, boolean primary) {
        Button button = new Button(this); button.setAllCaps(false); button.setText(value);
        button.setTextColor(primary ? 0xff10251b : FG); button.setBackground(round(primary ? GREEN : 0xff2b3544, 9));
        button.setMinHeight(dp(46)); button.setPadding(dp(12), 0, dp(12), 0); return button;
    }
    private GradientDrawable round(int color, int radius) {
        GradientDrawable shape = new GradientDrawable(); shape.setColor(color); shape.setCornerRadius(dp(radius)); return shape;
    }
}
