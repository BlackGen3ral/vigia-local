package br.com.artho.vigialocal;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.Locale;

final class VigiaApi {
    static final String ORIGIN = "https://vigia-local.pages.dev";
    static final String APK = "https://github.com/BlackGen3ral/vigia-local/releases/latest/download/VigiaLocal-Android.apk";
    static final String RELEASES = "https://api.github.com/repos/BlackGen3ral/vigia-local/releases/latest";

    static String pair(String input) throws Exception {
        String code = input.trim();
        if (code.startsWith("https://")) {
            String fragment = URI.create(code).getFragment();
            code = fragment != null && fragment.startsWith("pair=") ? fragment.substring(5) : "";
        }
        if (code.matches("[a-f0-9]{64}")) return code;
        code = code.toUpperCase(Locale.ROOT).replaceAll("[\\s-]", "");
        if (!code.matches("[0-9A-HJKMNP-TV-Z]{12}"))
            throw new IOException("Digite os 12 caracteres do convite gerado no computador da loja.");
        return json(ORIGIN + "/api/invite/redeem", null, new JSONObject().put("code", code)).getString("token");
    }

    static JSONObject events(String token) throws Exception { return json(ORIGIN + "/api/events", token, null); }
    static JSONObject arrivalsAfter(String token, long since) throws Exception {
        return json(ORIGIN + "/api/events?after=" + since, token, null);
    }
    static String latestVersion() throws Exception { return json(RELEASES, null, null).optString("tag_name"); }
    static Bitmap thumbnail(String event, String token) {
        if (!event.matches("[a-f0-9-]{36}")) return null;
        try {
            byte[] data = request(ORIGIN + "/api/event-thumbnail/" + event, token, null, 24 * 1024);
            return BitmapFactory.decodeByteArray(data, 0, data.length);
        } catch (Exception ignored) { return null; }
    }

    private static JSONObject json(String url, String token, JSONObject payload) throws Exception {
        return new JSONObject(new String(request(url, token, payload, 64 * 1024), StandardCharsets.UTF_8));
    }

    private static byte[] request(String url, String token, JSONObject payload, int limit) throws Exception {
        HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(6500);
        connection.setReadTimeout(6500);
        connection.setRequestProperty("User-Agent", "VigiaLocal-Android");
        if (token != null) connection.setRequestProperty("Authorization", "Bearer " + token);
        try {
            if (payload != null) {
                connection.setRequestMethod("POST");
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json");
                try (OutputStream output = connection.getOutputStream()) {
                    output.write(payload.toString().getBytes(StandardCharsets.UTF_8));
                }
            }
            int status = connection.getResponseCode();
            if (status >= 400 && status < 500)
                throw new IOException(status == 401 || status == 404
                    ? "Acesso inválido ou convite vencido." : "Confira o código ou tente novamente.");
            if (status != 200) throw new IOException("O serviço está indisponível agora.");
            try (InputStream input = connection.getInputStream(); ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                byte[] buffer = new byte[4096];
                for (int count; (count = input.read(buffer)) != -1;) {
                    if (output.size() + count > limit) throw new IOException("Resposta maior que o limite.");
                    output.write(buffer, 0, count);
                }
                return output.toByteArray();
            }
        } finally { connection.disconnect(); }
    }

    private VigiaApi() {}
}
