package com.softwaretoolkit.lanbridge;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.Build;

import java.util.Locale;
import java.util.UUID;

final class DeviceStore {
    private static final String PREFS = "lan_bridge";
    private static final String KEY_ID = "device_id";
    private static final String KEY_NAME = "device_name";
    private static final String KEY_ENABLED = "service_enabled";

    private DeviceStore() { }

    static SharedPreferences prefs(Context context) {
        return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    static String getId(Context context) {
        SharedPreferences preferences = prefs(context);
        String id = preferences.getString(KEY_ID, "");
        if (id == null || id.length() < 8) {
            id = UUID.randomUUID().toString().replace("-", "").substring(0, 12).toLowerCase(Locale.US);
            preferences.edit().putString(KEY_ID, id).apply();
        }
        return id;
    }

    static String getName(Context context) {
        String fallback = Build.MANUFACTURER + " " + Build.MODEL;
        String name = prefs(context).getString(KEY_NAME, fallback.trim());
        return name == null || name.trim().isEmpty() ? "Android 设备" : name.trim();
    }

    static void setName(Context context, String name) {
        if (name != null && !name.trim().isEmpty()) {
            prefs(context).edit().putString(KEY_NAME, name.trim()).apply();
        }
    }

    static boolean isEnabled(Context context) {
        return prefs(context).getBoolean(KEY_ENABLED, true);
    }

    static void setEnabled(Context context, boolean enabled) {
        prefs(context).edit().putBoolean(KEY_ENABLED, enabled).apply();
    }

    static void saveRequest(Context context, String id, String json) {
        prefs(context).edit().putString("request_" + id, json).apply();
    }

    static void removeRequest(Context context, String id) {
        prefs(context).edit().remove("request_" + id).apply();
    }
}
