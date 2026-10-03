package com.ankiassistant;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;

import java.io.File;
import java.io.FileNotFoundException;

/**
 * 极简 ContentProvider：把下载到 cache/update/ 的 APK 用 content:// 暴露给系统安装器。
 *
 * Android 7 起不允许把 file:// 交给别的应用（FileUriExposedException），
 * 标准做法是用 FileProvider —— 但本工程不使用任何第三方库，所以照 StudyCompanion 的做法
 * 自己实现一个最小版本。
 */
public class ApkProvider extends ContentProvider {

    public static final String AUTHORITY = "com.ankiassistant.apk";
    public static final String DIR = "update";

    public static Uri uriFor(String name) {
        return Uri.parse("content://" + AUTHORITY + "/" + name);
    }

    public static File fileFor(android.content.Context ctx, String name) {
        File dir = new File(ctx.getCacheDir(), DIR);
        if (!dir.exists()) dir.mkdirs();
        return new File(dir, name);
    }

    @Override
    public boolean onCreate() { return true; }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        String name = uri.getLastPathSegment();
        File f = fileFor(getContext(), name);
        if (!f.exists()) throw new FileNotFoundException(f.getAbsolutePath());
        return ParcelFileDescriptor.open(f, ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override
    public String getType(Uri uri) {
        return "application/vnd.android.package-archive";
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] args, String sort) {
        return null;
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) { return null; }

    @Override
    public int delete(Uri uri, String selection, String[] args) { return 0; }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] args) { return 0; }
}
