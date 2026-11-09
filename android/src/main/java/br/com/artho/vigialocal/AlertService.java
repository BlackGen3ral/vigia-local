package br.com.artho.vigialocal;

import android.app.*;
import android.content.*;
import android.graphics.Bitmap;
import android.media.AudioAttributes;
import android.media.RingtoneManager;
import android.os.IBinder;
import org.json.*;
import java.util.concurrent.*;

public final class AlertService extends Service {
    static final String PREFS = "vigia";
    static final String ENABLED = "alerts";
    static final String TOKEN = "store";
    static final String CURSOR = "cursor";
    private static final String BACKGROUND = "monitor";
    private static final String ARRIVALS = "chegadas";
    private ScheduledExecutorService worker;

    static void channels(Context context) {
        NotificationManager manager = context.getSystemService(NotificationManager.class);
        NotificationChannel background = new NotificationChannel(BACKGROUND, "Vigia em segundo plano", NotificationManager.IMPORTANCE_LOW);
        NotificationChannel arrivals = new NotificationChannel(ARRIVALS, "Chegadas e testes", NotificationManager.IMPORTANCE_HIGH);
        arrivals.enableVibration(true);
        arrivals.setLockscreenVisibility(Notification.VISIBILITY_PRIVATE);
        arrivals.setSound(RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION),
            new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_NOTIFICATION).build());
        manager.createNotificationChannel(background);
        manager.createNotificationChannel(arrivals);
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        if (!prefs.getBoolean(ENABLED, false) || prefs.getString(TOKEN, null) == null) {
            stopSelf();
            return START_NOT_STICKY;
        }
        channels(this);
        startForeground(100, new Notification.Builder(this, BACKGROUND).setSmallIcon(R.drawable.ic_alert)
            .setContentTitle("Vigia Local ativo").setContentText("Buscando avisos da sua loja.")
            .setOngoing(true).setContentIntent(open()).build());
        if (worker == null) {
            worker = Executors.newSingleThreadScheduledExecutor();
            worker.scheduleWithFixedDelay(() -> poll(prefs), 0, 25, TimeUnit.SECONDS);
        }
        return START_STICKY;
    }

    private void poll(SharedPreferences prefs) {
        String token = prefs.getString(TOKEN, null);
        if (token == null || !prefs.getBoolean(ENABLED, false)) return;
        try {
            long since = prefs.getLong(CURSOR, 0);
            JSONArray events = VigiaApi.arrivalsAfter(token, since).getJSONArray("events");
            long newest = since;
            for (int i = events.length() - 1; i >= 0; i--) {
                JSONObject event = events.getJSONObject(i);
                long time = event.getLong("time");
                newest = Math.max(time, newest);
                if (time > since && time >= System.currentTimeMillis() - 120000) {
                    if (!token.equals(prefs.getString(TOKEN, null)) || !prefs.getBoolean(ENABLED, false)) return;
                    showArrival(event, token);
                }
            }
            if (token.equals(prefs.getString(TOKEN, null))) prefs.edit().putLong(CURSOR, newest).apply();
        } catch (Exception ignored) {
            // Keep the cursor so the next successful request can catch up.
        }
    }

    private void showArrival(JSONObject event, String token) {
        boolean test = "test".equals(event.optString("kind"));
        String id = event.optString("id");
        Notification.Builder notification = new Notification.Builder(this, ARRIVALS)
            .setSmallIcon(R.drawable.ic_alert)
            .setContentTitle(test ? "Teste do Vigia Local" : "Pessoa na entrada")
            .setContentText(test ? "Seu celular recebeu o aviso de teste." : "Uma pessoa passou pela entrada da loja.")
            .setVisibility(Notification.VISIBILITY_PRIVATE).setAutoCancel(true).setContentIntent(open());
        if (!test && event.optBoolean("hasThumbnail")) {
            Bitmap image = VigiaApi.thumbnail(id, token);
            if (image != null) notification.setStyle(new Notification.BigPictureStyle().bigPicture(image));
        }
        getSystemService(NotificationManager.class).notify(id.hashCode(), notification.build());
    }

    private PendingIntent open() {
        Intent intent = new Intent(this, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP);
        return PendingIntent.getActivity(this, 0, intent, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
    }
    @Override public IBinder onBind(Intent intent) { return null; }
    @Override public void onDestroy() {
        if (worker != null) worker.shutdownNow();
        super.onDestroy();
    }
}
