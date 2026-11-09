package br.com.artho.vigialocal;

import android.content.Context;
import android.content.pm.*;
import android.os.*;
import java.io.*;
import java.net.*;
import java.lang.ref.WeakReference;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.concurrent.*;
import org.json.JSONObject;

final class AndroidUpdate {
    interface Listener { void updateChanged(); }
    private static AndroidUpdate instance;
    static synchronized AndroidUpdate get(Context context) {
        if (instance == null) instance = new AndroidUpdate(context.getApplicationContext());
        return instance;
    }
    private final Context context;
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler main = new Handler(Looper.getMainLooper());
    private WeakReference<Listener> listener = new WeakReference<>(null);
    volatile boolean busy, ready, failed, prompted;
    volatile String message = "Atualizações baixadas automaticamente ao abrir o app.";
    private volatile long lastCheck;
    private AndroidUpdate(Context context) { this.context = context; }
    void attach(Listener value) { listener = new WeakReference<>(value); value.updateChanged(); }
    void detach(Listener value) { if (listener.get() == value) listener.clear(); }
    private void changed() { main.post(() -> { Listener value=listener.get(); if(value!=null) value.updateChanged(); }); }
    synchronized void check(boolean force) {
        if (busy || ready || (!force && System.currentTimeMillis()-lastCheck < 30*60000)) return;
        busy = true; failed = false; message = "Verificando atualizações…"; changed();
        worker.execute(() -> {
            File partial = new File(UpdateProvider.apk(context).getParentFile(), "download.partial.apk");
            try {
                JSONObject release = VigiaApi.update();
                String version=release.getString("version");
                PackageInfo installed=context.getPackageManager().getPackageInfo(context.getPackageName(),0);
                if (!UpdateVersion.newer(version,installed.versionName)) {
                    message="Você está na versão mais recente."; lastCheck=System.currentTimeMillis(); return;
                }
                long size=release.getLong("size");
                String checksum=release.getString("sha256");
                String url=release.getString("url");
                if(size<=0 || size>64*1024*1024 || !checksum.matches("[a-f0-9]{64}") ||
                    !url.equals(VigiaApi.ORIGIN+"/api/android/apk/v"+version)) throw new IOException("Invalid update manifest");
                File apk=UpdateProvider.apk(context);
                if(!apk.getParentFile().isDirectory() && !apk.getParentFile().mkdirs()) throw new IOException("Storage unavailable");
                if(!apk.isFile() || apk.length()!=size || !checksum.equals(digest(apk))) {
                    HttpURLConnection connection=(HttpURLConnection)new URL(url).openConnection();
                    connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(10000);connection.setReadTimeout(20000);
                    try {
                        if(connection.getResponseCode()!=200) throw new IOException("Download unavailable");
                        long received=0;int lastPercent=-1;
                        try(InputStream input=connection.getInputStream();FileOutputStream output=new FileOutputStream(partial)) {
                            byte[] buffer=new byte[16384];
                            for(int count;(count=input.read(buffer))!=-1;) {
                                received+=count;if(received>size) throw new IOException("Unexpected APK size");
                                output.write(buffer,0,count);
                                int percent=(int)(received*100/size);
                                if(percent!=lastPercent) {lastPercent=percent;message="Baixando versão "+version+"… "+percent+"%";changed();}
                            }
                            output.getFD().sync();
                        }
                        if(received!=size || !checksum.equals(digest(partial))) throw new IOException("Invalid APK checksum");
                        verifyPackage(partial, installed, version);
                        Files.move(partial.toPath(),apk.toPath(),StandardCopyOption.REPLACE_EXISTING);
                    } finally { connection.disconnect(); }
                }
                verifyPackage(apk,installed,version);
                ready=true;message="Versão "+version+" pronta. Confirme a instalação no Android.";
                lastCheck=System.currentTimeMillis();
            } catch(Exception e) {
                lastCheck=System.currentTimeMillis()-25*60000; // Retry automatically after five minutes.
                failed=true;message="Não foi possível baixar ou verificar a atualização. Toque para tentar novamente.";
            } finally {partial.delete();busy=false;changed();}
        });
    }
    private static String digest(File file) throws Exception {
        MessageDigest hash=MessageDigest.getInstance("SHA-256");
        try(InputStream input=new FileInputStream(file)) {byte[] bytes=new byte[16384];for(int n;(n=input.read(bytes))!=-1;)hash.update(bytes,0,n);}
        StringBuilder result=new StringBuilder();for(byte b:hash.digest())result.append(String.format(java.util.Locale.ROOT,"%02x",b&255));return result.toString();
    }
    @SuppressWarnings("deprecation")
    private void verifyPackage(File file, PackageInfo installed, String version) throws Exception {
        PackageManager manager=context.getPackageManager();
        PackageInfo archive=manager.getPackageArchiveInfo(file.getAbsolutePath(),PackageManager.GET_SIGNATURES);
        PackageInfo signedInstalled=manager.getPackageInfo(context.getPackageName(),PackageManager.GET_SIGNATURES);
        if(archive==null || !context.getPackageName().equals(archive.packageName) || !version.equals(archive.versionName) ||
            archive.versionCode<=installed.versionCode || archive.signatures==null || signedInstalled.signatures==null ||
            !Arrays.equals(archive.signatures,signedInstalled.signatures)) throw new IOException("APK identity mismatch");
    }
}
