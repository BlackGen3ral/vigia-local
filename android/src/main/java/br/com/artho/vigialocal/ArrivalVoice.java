package br.com.artho.vigialocal;

import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Context;
import android.content.res.AssetFileDescriptor;
import android.media.AudioAttributes;
import android.media.AudioManager;
import android.media.MediaPlayer;
import android.os.Handler;
import android.os.Looper;
import android.speech.tts.TextToSpeech;
import java.io.IOException;
import java.util.Locale;

final class ArrivalVoice {
    private static final AudioAttributes AUDIO = new AudioAttributes.Builder()
        .setUsage(AudioAttributes.USAGE_NOTIFICATION)
        .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build();
    private final Handler main = new Handler(Looper.getMainLooper());
    private final Context context;
    private TextToSpeech speech;
    private MediaPlayer player;
    private volatile boolean ready;
    private volatile boolean closed;

    ArrivalVoice(Context context) {
        this.context = context;
        speech = new TextToSpeech(context, result -> main.post(() -> {
            if (closed || result != TextToSpeech.SUCCESS) return;
            ready = speech.setLanguage(new Locale("pt", "BR")) >= TextToSpeech.LANG_AVAILABLE;
            speech.setAudioAttributes(AUDIO);
        }));
    }

    static String message(int people) {
        return people == 1 ? "Uma pessoa chegou." : people + " pessoas chegaram.";
    }

    boolean announce(int people, String channelId) {
        if (closed) return false;
        var notifications = context.getSystemService(NotificationManager.class);
        var audio = context.getSystemService(AudioManager.class);
        NotificationChannel channel = notifications.getNotificationChannel(channelId);
        if (!notifications.areNotificationsEnabled() || channel == null
            || channel.getImportance() < NotificationManager.IMPORTANCE_DEFAULT || channel.getSound() == null
            || audio.getRingerMode() != AudioManager.RINGER_MODE_NORMAL
            || notifications.getCurrentInterruptionFilter() != NotificationManager.INTERRUPTION_FILTER_ALL) return false;

        AssetFileDescriptor recording = null;
        try { recording = context.getAssets().openFd(people + ".mp3"); }
        catch (IOException ignored) { /* Device speech is available without a packaged clip. */ }
        if (recording == null && !ready) return false;
        var clip = recording;
        main.post(() -> {
            if (clip != null) {
                try (clip) {
                    if (closed) return;
                    releasePlayer();
                    if (ready) speech.stop();
                    player = new MediaPlayer();
                    player.setAudioAttributes(AUDIO);
                    player.setDataSource(clip.getFileDescriptor(), clip.getStartOffset(), clip.getLength());
                    player.setOnPreparedListener(MediaPlayer::start);
                    player.setOnCompletionListener(done -> releasePlayer());
                    player.setOnErrorListener((failed, what, extra) -> {
                        releasePlayer();
                        speak(people);
                        return true;
                    });
                    player.prepareAsync();
                    return;
                } catch (IOException | RuntimeException e) { releasePlayer(); }
            }
            speak(people);
        });
        return true;
    }

    private void speak(int people) {
        if (ready && !closed) speech.speak(message(people), TextToSpeech.QUEUE_FLUSH, null, "vigia-arrival");
    }

    private void releasePlayer() {
        if (player == null) return;
        player.release();
        player = null;
    }

    void close() {
        closed = true;
        releasePlayer();
        speech.shutdown();
    }
}
