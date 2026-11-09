package br.com.artho.vigialocal;

import android.content.*;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.*;

/** Shares only the verified update APK with the Android installer. */
public final class UpdateProvider extends ContentProvider {
    static File apk(Context context) { return new File(new File(context.getCacheDir(), "updates"), "verified.apk"); }
    static Uri uri(Context context) { return Uri.parse("content://" + context.getPackageName() + ".updates/verified.apk"); }
    private void validate(Uri uri) throws FileNotFoundException {
        if (!uri(getContext()).equals(uri) || !apk(getContext()).isFile()) throw new FileNotFoundException();
    }
    @Override public boolean onCreate() { return true; }
    @Override public String getType(Uri uri) { return "application/vnd.android.package-archive"; }
    @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        validate(uri);
        if (!"r".equals(mode)) throw new FileNotFoundException("Read only");
        return ParcelFileDescriptor.open(apk(getContext()), ParcelFileDescriptor.MODE_READ_ONLY);
    }
    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] args, String order) {
        try { validate(uri); } catch (FileNotFoundException e) { return null; }
        String[] columns = projection != null ? projection : new String[]{OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE};
        MatrixCursor cursor = new MatrixCursor(columns);
        Object[] values = new Object[columns.length];
        for (int i=0;i<columns.length;i++) {
            if (OpenableColumns.DISPLAY_NAME.equals(columns[i])) values[i] = "VigiaLocal-Atualizacao.apk";
            else if (OpenableColumns.SIZE.equals(columns[i])) values[i] = apk(getContext()).length();
        }
        cursor.addRow(values); return cursor;
    }
    @Override public Uri insert(Uri uri, ContentValues values) { throw new UnsupportedOperationException(); }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] args) { throw new UnsupportedOperationException(); }
    @Override public int delete(Uri uri, String selection, String[] args) { throw new UnsupportedOperationException(); }
}
